using System.Text;
using System.Text.Json;
using api.LoRa;
using api.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace api.Gateway.ChirpStack
{
    /// Profile B (LoRaGateway) only - subscribes to ChirpStack's MQTT uplink topic, forwards each through the same /api/Gateway/Batch path Profile A uses, and publishes the result back as a downlink; UNTESTED against any real ChirpStack instance, gateway, or LoRa device - treat as a first draft, not a working integration.
    public sealed partial class ChirpStackUplinkService(
        AgrumyServiceClient client, IOptions<GatewayOptions> options, ILogger<ChirpStackUplinkService> logger)
        : BackgroundService
    {
        private readonly ChirpStackOptions cs = options.Value.ChirpStack;
        private IMqttClient? mqtt;
        private Dictionary<string, GatewayDeviceMapping> mappingByDevEui = new();

        [LoggerMessage(Level = LogLevel.Information, Message = "Connected to ChirpStack MQTT at {Host}:{Port}, subscribed to {Topic}.")]
        private static partial void LogConnected(ILogger logger, string host, int port, string topic);

        [LoggerMessage(Level = LogLevel.Warning, Message = "ChirpStack MQTT connect failed, retrying in 30s.")]
        private static partial void LogConnectFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Refreshed DevEUI mapping: {Count} entries.")]
        private static partial void LogMappingRefreshed(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not refresh DevEUI mapping - keeping the previous cache.")]
        private static partial void LogMappingRefreshFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process a ChirpStack uplink - skipped.")]
        private static partial void LogUplinkFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Uplink from unmapped DevEUI {DevEui} - dropped.")]
        private static partial void LogUnmappedDevEui(ILogger logger, string? devEui);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Uplink from {DevEui} had no 'data' field - dropped.")]
        private static partial void LogNoDataField(ILogger logger, string devEui);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Uplink from {DevEui} had an unrecognized envelope type {Type} - dropped.")]
        private static partial void LogUnrecognizedEnvelope(ILogger logger, string devEui, string? type);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RefreshMappingAsync(stoppingToken);
            _ = MappingRefreshLoopAsync(stoppingToken);

            var factory = new MqttFactory();
            mqtt = factory.CreateMqttClient();

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(cs.MqttHost, cs.MqttPort)
                .WithCleanSession();
            if (!string.IsNullOrEmpty(cs.MqttUsername))
            {
                optionsBuilder = optionsBuilder.WithCredentials(cs.MqttUsername, cs.MqttPassword);
            }
            var clientOptions = optionsBuilder.Build();

            mqtt.ApplicationMessageReceivedAsync += OnUplinkAsync;

            string uplinkTopic = $"application/{cs.ApplicationId}/device/+/event/up";
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await mqtt.ConnectAsync(clientOptions, stoppingToken);
                    await mqtt.SubscribeAsync(uplinkTopic, cancellationToken: stoppingToken);
                    LogConnected(logger, cs.MqttHost, cs.MqttPort, uplinkTopic);
                    break;
                }
                catch (Exception ex)
                {
                    LogConnectFailed(logger, ex);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }

            // Keep the service alive; MQTTnet's own client handles the receive loop via the event above.
            await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        private async Task MappingRefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, cs.MappingRefreshSeconds)), ct);
                await RefreshMappingAsync(ct);
            }
        }

        private async Task RefreshMappingAsync(CancellationToken ct)
        {
            try
            {
                IList<GatewayDeviceMapping> mappings = await client.GetDeviceMappingAsync(ct);
                mappingByDevEui = mappings
                    .Where(m => !string.IsNullOrEmpty(m.DevEUI))
                    .ToDictionary(m => m.DevEUI!.ToUpperInvariant());
                LogMappingRefreshed(logger, mappingByDevEui.Count);
            }
            catch (Exception ex)
            {
                LogMappingRefreshFailed(logger, ex);
            }
        }

        private async Task OnUplinkAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                await HandleUplinkAsync(args.ApplicationMessage.PayloadSegment.ToArray());
            }
            catch (Exception ex)
            {
                // One malformed/unrecognized uplink must never take the MQTT loop down - same "skip the poison entry" reasoning as GatewayApiController.Batch's per-entry try/catch.
                LogUplinkFailed(logger, ex);
            }
        }

        private async Task HandleUplinkAsync(byte[] payloadBytes)
        {
            using JsonDocument doc = JsonDocument.Parse(payloadBytes);
            JsonElement root = doc.RootElement;

            string? devEui = root.TryGetProperty("deviceInfo", out var deviceInfo) && deviceInfo.TryGetProperty("devEui", out var devEuiEl)
                ? devEuiEl.GetString()
                : null;
            if (string.IsNullOrEmpty(devEui) || !mappingByDevEui.TryGetValue(devEui.ToUpperInvariant(), out GatewayDeviceMapping? mapping))
            {
                LogUnmappedDevEui(logger, devEui);
                return;
            }

            int sf = TryGetSpreadingFactor(root) ?? 12; // unknown SF - assume worst case (safest duty-cycle-wise)

            // Placeholder wire format (see class remarks) - base64-decoded "data" is small JSON: {"t":"config"|"sensor"|"event"|"ack", ...fields matching the equivalent GatewayEntryType's HTTP payload}.
            if (!root.TryGetProperty("data", out var dataEl) || dataEl.GetString() is not string base64)
            {
                LogNoDataField(logger, devEui);
                return;
            }
            byte[] frmPayload = Convert.FromBase64String(base64);
            using JsonDocument envelope = JsonDocument.Parse(frmPayload);
            string? entryTypeTag = envelope.RootElement.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
            GatewayEntryType? entryType = entryTypeTag switch
            {
                "config" => GatewayEntryType.Config,
                "sensor" => GatewayEntryType.SensorData,
                "event" => GatewayEntryType.Event,
                "ack" => GatewayEntryType.CommandAck,
                _ => null,
            };
            if (entryType is null)
            {
                LogUnrecognizedEnvelope(logger, devEui, entryTypeTag);
                return;
            }

            var entry = new GatewayBatchEntry
            {
                DeviceApiId = mapping.DeviceApiId,
                DeviceApiKey = mapping.DeviceApiKey,
                Type = entryType.Value,
                Payload = envelope.RootElement.Clone(),
            };
            GatewayBatchResponse response = await client.BatchAsync(new GatewayBatchRequest { Entries = [entry] }, CancellationToken.None);
            GatewayBatchEntryResult? result = response.Results.FirstOrDefault();

            await PublishDownlinkAsync(devEui, result, sf);
        }

        private static int? TryGetSpreadingFactor(JsonElement root) =>
            root.TryGetProperty("txInfo", out var txInfo) &&
            txInfo.TryGetProperty("modulation", out var modulation) &&
            modulation.TryGetProperty("lora", out var lora) &&
            lora.TryGetProperty("spreadingFactor", out var sfEl) &&
            sfEl.TryGetInt32(out int sf)
                ? sf
                : null;

        /// Class A: ChirpStack itself queues this for the device's next RX window - Gateway just publishes once, it never manages the actual radio timing.
        private async Task PublishDownlinkAsync(string devEui, GatewayBatchEntryResult? result, int sf)
        {
            if (mqtt is null || !mqtt.IsConnected)
            {
                return;
            }

            var downlink = new
            {
                confirmed = false,
                fPort = 1,
                // "empty" success (Config with nothing new) needs no downlink at all - Class A
                // downlink slots are scarce, do not spend one on nothing.
                data = result is { Success: true, Config: not null } || result is { Success: false }
                    ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        ok = result?.Success ?? false,
                        // Hint for the (not-yet-existing) LoRa firmware profile: how long to wait before its next config-poll uplink, scaled to this uplink's own SF - see api.LoRa.LoRaInterval.
                        retryAfterSeconds = (int)LoRaInterval.ForSpreadingFactor(sf).TotalSeconds,
                    })))
                    : null,
            };

            string topic = $"application/{cs.ApplicationId}/device/{devEui}/command/down";
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(JsonSerializer.SerializeToUtf8Bytes(downlink))
                .Build();
            await mqtt.PublishAsync(message, CancellationToken.None);
        }
    }
}
