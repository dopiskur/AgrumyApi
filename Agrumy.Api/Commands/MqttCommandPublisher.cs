using System.Text.Json;
using api.Dal.Interface;
using api.Models;
using MQTTnet;
using MQTTnet.Client;

namespace api.Commands
{
    public interface IMqttCommandPublisher
    {
        Task PublishAsync(Device device, PendingCommand command, CancellationToken ct = default);
    }

    /// Pure topic convention matching AgrumyFirmware's MqttController subscribe topic exactly, kept separate from MqttCommandPublisher so the wire contract is testable without a broker.
    public static class MqttCommandTopic
    {
        public static string ForDevice(int tenantId, int deviceId) => $"agrumy/{tenantId}/{deviceId}/command";
    }

    /// Best-effort instant command delivery over MQTT alongside the HTTP/JWT poll cycle - never the only way a command reaches a device, since CommandQueueService.GetPendingCommandAsync's next poll response always carries it too; a no-op when MqttTransportEnabled is off or unconfigured, and any broker/network failure is swallowed so it can never fail the triggering request.
    public sealed class MqttCommandPublisher(IRepository repo, ILogger<MqttCommandPublisher> logger) : IMqttCommandPublisher
    {
        public async Task PublishAsync(Device device, PendingCommand command, CancellationToken ct = default)
        {
            ServerConfig serverConfig = await repo.ServerConfigGetAsync(1);
            if (!serverConfig.MqttTransportEnabled || string.IsNullOrWhiteSpace(serverConfig.MqttBrokerHost) || device.IDDevice is not int deviceId)
            {
                return;
            }

            string topic = MqttCommandTopic.ForDevice(device.TenantID, deviceId);
            // ConditionConfigJson.Options camelCases properties and leaves ActionType as its int, matching the shape the HTTP config-poll response already sends this same type as.
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(command, ConditionConfigJson.Options);

            try
            {
                var factory = new MqttFactory();
                using IMqttClient client = factory.CreateMqttClient();
                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(serverConfig.MqttBrokerHost, serverConfig.MqttBrokerPort)
                    .WithCleanSession();
                if (!string.IsNullOrEmpty(serverConfig.MqttUsername))
                {
                    optionsBuilder = optionsBuilder.WithCredentials(serverConfig.MqttUsername, serverConfig.MqttPassword);
                }
                await client.ConnectAsync(optionsBuilder.Build(), ct);

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .Build();
                await client.PublishAsync(message, ct);
                await client.DisconnectAsync(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MQTT instant command push failed for device {DeviceId} - falling back to normal poll delivery.", deviceId);
            }
        }
    }
}
