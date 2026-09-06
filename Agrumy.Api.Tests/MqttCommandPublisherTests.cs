using System.Text.Json;
using System.Text.Json.Nodes;
using api.Commands;
using api.Dal.Interface;
using api.Models;
using Moq;

namespace Agrumy.Api.Tests;

/// MqttCommandTopic's wire contract (must match AgrumyFirmware's MqttController subscribe topic exactly)
/// and CommandQueueService's wiring to IMqttCommandPublisher - the actual network call is untestable
/// without a broker, same status as ChirpStackUplinkService.
public class MqttCommandPublisherTests
{
    [Fact]
    public void ForDevice_BuildsExpectedTopic()
    {
        Assert.Equal("agrumy/3/500/command", MqttCommandTopic.ForDevice(3, 500));
    }

    private readonly Mock<ICommandRepository> _commands = new(MockBehavior.Strict);
    private readonly Mock<IDeviceRepository> _devices = new(MockBehavior.Strict);
    private readonly Mock<IDeviceFarmUnitRepository> _units = new(MockBehavior.Strict);
    private readonly Mock<IMqttCommandPublisher> _mqtt = new(MockBehavior.Strict);

    private CommandQueueService NewService() => new(_commands.Object, _devices.Object, _units.Object, _mqtt.Object);

    private static Device ControllerDevice(int id) => new() { IDDevice = id, DeviceControllerEnabled = true };

    [Fact]
    public async Task IssueCommand_Success_PublishesToMqtt()
    {
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync(ControllerDevice(500));
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(1);
        _mqtt.Setup(m => m.PublishAsync(
            It.Is<Device>(d => d.IDDevice == 500),
            It.Is<PendingCommand>(p => p.IDDeviceCommand == 1 && p.ActionType == CommandActionType.Reboot),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Device, 500, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.Success, result.Outcome);
        _mqtt.Verify(m => m.PublishAsync(It.IsAny<Device>(), It.IsAny<PendingCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IssueCommand_Deduplicated_NeverPublishesToMqtt()
    {
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync(ControllerDevice(500));
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(true);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Device, 500, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        // Strict mock: PublishAsync was never set up - a deduplicated command must not publish.
    }

    // ---- #363 HMAC signing --------------------------------------------

    private static JsonNode SampleCommandNode() =>
        JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(
            new PendingCommand { IDDeviceCommand = 7, ActionType = CommandActionType.Reboot, ExpiresAt = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc), Payload = "abc" },
            ConditionConfigJson.Options))!;

    [Fact]
    public void CanonicalString_JoinsFieldsInFixedOrder()
    {
        string canonical = MqttCommandPublisher.CanonicalString(SampleCommandNode());

        Assert.Equal($"7|{(int)CommandActionType.Reboot}|2026-09-06T12:00:00Z|abc", canonical);
    }

    [Fact]
    public void CanonicalString_NullPayload_UsesEmptyString()
    {
        JsonNode node = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(
            new PendingCommand { IDDeviceCommand = 7, ActionType = CommandActionType.Reboot, ExpiresAt = DateTime.UtcNow, Payload = null },
            ConditionConfigJson.Options))!;

        string canonical = MqttCommandPublisher.CanonicalString(node);

        Assert.EndsWith("|", canonical);
    }

    [Fact]
    public void ComputeSignature_IsDeterministic_AndDependsOnApiKey()
    {
        string sigA = MqttCommandPublisher.ComputeSignature("7|1|x|y", "device-key-a");
        string sigA2 = MqttCommandPublisher.ComputeSignature("7|1|x|y", "device-key-a");
        string sigB = MqttCommandPublisher.ComputeSignature("7|1|x|y", "device-key-b");

        Assert.Equal(sigA, sigA2);
        Assert.NotEqual(sigA, sigB);
    }
}
