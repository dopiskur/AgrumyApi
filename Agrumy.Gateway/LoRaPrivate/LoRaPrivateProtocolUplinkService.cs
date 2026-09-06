using System.IO.Ports;
using System.Text.Json;
using api.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace api.Gateway.LoRaPrivate
{
    /// GatewayProfile.LoRaPrivateProtocol - reads AgrumySerialFrame uplinks from a locally-attached
    /// ESP32+SX126x radio-frontend board (RadioLib raw PHY, not LoRaWAN/ChirpStack), forwards each
    /// through the same /api/Gateway/Batch path Profile A and ChirpStackUplinkService use, and writes
    /// the batch result back as a downlink frame - mirrors ChirpStackUplinkService's shape with the
    /// transport swapped (serial port instead of MQTT, a 16-bit node address instead of a DevEUI).
    public sealed partial class LoRaPrivateProtocolUplinkService(
        AgrumyServiceClient client, IOptions<GatewayOptions> options, ILogger<LoRaPrivateProtocolUplinkService> logger)
        : BackgroundService
    {
        private readonly LoRaPrivateProtocolOptions cfg = options.Value.LoRaPrivateProtocol;
        private Dictionary<ushort, GatewayDeviceMapping> mappingByAddress = new();
        private SerialPort? port;

        [LoggerMessage(Level = LogLevel.Information, Message = "Opened radio-frontend serial port {Port} at {BaudRate} baud.")]
        private static partial void LogPortOpened(ILogger logger, string port, int baudRate);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Radio-frontend serial port open failed, retrying in 30s.")]
        private static partial void LogPortOpenFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Refreshed node-address mapping: {Count} entries.")]
        private static partial void LogMappingRefreshed(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not refresh node-address mapping - keeping the previous cache.")]
        private static partial void LogMappingRefreshFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process a LoRa private-protocol uplink - skipped.")]
        private static partial void LogUplinkFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Uplink from unmapped node address {Address} - dropped.")]
        private static partial void LogUnmappedAddress(ILogger logger, ushort address);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Uplink from node {Address} had an unrecognized envelope type {Type} - dropped.")]
        private static partial void LogUnrecognizedEnvelope(ILogger logger, ushort address, string? type);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RefreshMappingAsync(stoppingToken);
            _ = MappingRefreshLoopAsync(stoppingToken);

            var readBuffer = new List<byte>(1024);
            var chunk = new byte[256];

            while (!stoppingToken.IsCancellationRequested)
            {
                if (port is null || !port.IsOpen)
                {
                    if (!TryOpenPort())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }
                }

                try
                {
                    int read = await port!.BaseStream.ReadAsync(chunk, stoppingToken);
                    if (read <= 0)
                    {
                        continue;
                    }
                    readBuffer.AddRange(chunk.AsSpan(0, read).ToArray());
                    ConsumeBuffer(readBuffer);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Board unplugged/power-cycled - drop the port and let the top of the loop reopen it.
                    LogPortOpenFailed(logger, ex);
                    port?.Dispose();
                    port = null;
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private bool TryOpenPort()
        {
            try
            {
                port = new SerialPort(cfg.SerialPort, cfg.BaudRate) { ReadTimeout = 5000, WriteTimeout = 5000 };
                port.Open();
                LogPortOpened(logger, cfg.SerialPort, cfg.BaudRate);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                LogPortOpenFailed(logger, ex);
                return false;
            }
        }

        /// Decodes as many complete frames as `readBuffer` currently holds, dispatching each and trimming the buffer as it goes - a partial trailing frame is left in place for the next read.
        private void ConsumeBuffer(List<byte> readBuffer)
        {
            while (readBuffer.Count > 0)
            {
                int consumed = AgrumySerialFrame.TryDecodeUplink(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(readBuffer), out var decoded);
                if (consumed == 0)
                {
                    break; // wait for more bytes
                }
                readBuffer.RemoveRange(0, consumed);
                if (decoded is { } uplink)
                {
                    _ = HandleUplinkAsync(uplink);
                }
            }
        }

        private async Task MappingRefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, cfg.MappingRefreshSeconds)), ct);
                await RefreshMappingAsync(ct);
            }
        }

        private async Task RefreshMappingAsync(CancellationToken ct)
        {
            try
            {
                IList<GatewayDeviceMapping> mappings = await client.GetDeviceMappingAsync(ct);
                mappingByAddress = mappings
                    .Where(m => ushort.TryParse(m.DevEUI, out _))
                    .ToDictionary(m => ushort.Parse(m.DevEUI!));
                LogMappingRefreshed(logger, mappingByAddress.Count);
            }
            catch (Exception ex)
            {
                LogMappingRefreshFailed(logger, ex);
            }
        }

        private async Task HandleUplinkAsync(AgrumySerialFrame.DecodedUplink uplink)
        {
            try
            {
                await ProcessUplinkAsync(uplink);
            }
            catch (Exception ex)
            {
                // One malformed/unmapped uplink must never take the read loop down - same reasoning as ChirpStackUplinkService.OnUplinkAsync.
                LogUplinkFailed(logger, ex);
            }
        }

        private async Task ProcessUplinkAsync(AgrumySerialFrame.DecodedUplink uplink)
        {
            if (!mappingByAddress.TryGetValue(uplink.SourceAddress, out GatewayDeviceMapping? mapping))
            {
                LogUnmappedAddress(logger, uplink.SourceAddress);
                return;
            }

            using JsonDocument envelope = JsonDocument.Parse(uplink.Payload);
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
                LogUnrecognizedEnvelope(logger, uplink.SourceAddress, entryTypeTag);
                return;
            }

            // Same reasoning as ChirpStackUplinkService: SensorData needs an array payload, the firmware nests readings under "d".
            JsonElement payload = entryType == GatewayEntryType.SensorData && envelope.RootElement.TryGetProperty("d", out var sensorArray)
                ? sensorArray.Clone()
                : envelope.RootElement.Clone();

            var entry = new GatewayBatchEntry
            {
                DeviceApiId = mapping.DeviceApiId,
                DeviceApiKey = mapping.DeviceApiKey,
                Type = entryType.Value,
                Payload = payload,
            };
            GatewayBatchResponse response = await client.BatchAsync(new GatewayBatchRequest { Entries = [entry] }, CancellationToken.None);
            GatewayBatchEntryResult? result = response.Results.FirstOrDefault();

            await SendDownlinkAsync(uplink.SourceAddress, result);
        }

        /// No Class A RX-window timing to respect here (that's a LoRaWAN concept) - the radio-frontend queues this for its next transmit opportunity after the node's own receive window, per LoRaPrivateController's fixed post-uplink listen slot.
        private async Task SendDownlinkAsync(ushort nodeAddress, GatewayBatchEntryResult? result)
        {
            if (port is not { IsOpen: true })
            {
                return;
            }

            // "Empty" success (Config with nothing new) skips the downlink, same as ChirpStackUplinkService.
            if (result is not ({ Success: true, Config: not null } or { Success: false }))
            {
                return;
            }

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                ok = result?.Success ?? false,
            });
            byte[] frame = AgrumySerialFrame.EncodeDownlink(nodeAddress, payload);
            await port.BaseStream.WriteAsync(frame, CancellationToken.None);
        }

        public override void Dispose()
        {
            port?.Dispose();
            base.Dispose();
        }
    }
}
