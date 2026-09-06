using api.Commands;
using api.Dal.Interface;
using api.Models;
using Moq;

namespace Agrumy.Api.Tests;

/// Exercises CommandQueueService directly - no HTTP/controller plumbing, no database (repositories are mocked).
public class CommandQueueServiceTests
{
    private readonly Mock<ICommandRepository> _commands = new(MockBehavior.Strict);
    private readonly Mock<IDeviceRepository> _devices = new(MockBehavior.Strict);
    private readonly Mock<IDeviceFarmUnitRepository> _units = new(MockBehavior.Strict);

    private CommandQueueService NewService() => new(_commands.Object, _devices.Object, _units.Object, new NoOpMqttCommandPublisher());

    private static Device ControllerDevice(int id) => new() { IDDevice = id, DeviceControllerEnabled = true };


    [Fact]
    public async Task Device_Target_Not_Found_Returns_TargetNotFound()
    {
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync((Device?)null);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Device, 500, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.TargetNotFound, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
    }

    [Fact]
    public async Task Zone_With_No_Controller_Returns_TargetNotFound()
    {
        // A zone has at most one controller - null means genuinely none, not "not looked up yet".
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync((Device?)null);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Zone, 10, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.TargetNotFound, result.Outcome);
    }

    [Fact]
    public async Task Zone_Target_Resolves_To_Its_Single_Controller()
    {
        DateTime before = DateTime.UtcNow;
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(500));
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(1);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Zone, 10, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.Success, result.Outcome);
        Assert.Equal([1], result.CreatedCommandIds);
        _commands.Verify(c => c.AddCommandAsync(500, CommandActionType.Reboot,
            It.Is<DateTime>(d => d >= before), It.Is<DateTime>(d => d > before)), Times.Once);
    }

    [Fact]
    public async Task Unit_With_No_Controllers_Across_Any_Zone_Returns_TargetNotFound()
    {
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(7)).ReturnsAsync(new List<Device>());

        var result = await NewService().IssueCommandAsync(CommandTargetType.Unit, 7, CommandActionType.ForceOTA);

        Assert.Equal(IssueCommandOutcome.TargetNotFound, result.Outcome);
    }

    [Fact]
    public async Task Unit_Target_FansOut_To_Every_Controller_Across_All_Its_Zones()
    {
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(7)).ReturnsAsync(new List<Device> { ControllerDevice(500), ControllerDevice(501) });
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.ForceOTA, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.HasActiveCommandAsync(501, CommandActionType.ForceOTA, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.ForceOTA, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(1);
        _commands.Setup(c => c.AddCommandAsync(501, CommandActionType.ForceOTA, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(2);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Unit, 7, CommandActionType.ForceOTA);

        Assert.Equal(IssueCommandOutcome.Success, result.Outcome);
        Assert.Equal([1, 2], result.CreatedCommandIds);
    }


    [Fact]
    public async Task Device_With_Active_Command_Of_Same_ActionType_Is_Deduplicated()
    {
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync(ControllerDevice(500));
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(true);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Device, 500, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
        // Strict mock: AddCommandAsync was never set up - a call to it here would throw.
    }

    /// HasActiveCommandAsync is only a fast-path - a null from AddCommandAsync (lost the DB-level race) must be treated as a dedup skip too.
    [Fact]
    public async Task Device_Losing_The_DB_Level_Dedup_Race_Is_Treated_As_AllDuplicates_Not_A_Crash()
    {
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync(ControllerDevice(500));
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync((int?)null);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Device, 500, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
    }

    [Fact]
    public async Task Unit_FanOut_One_Zone_Already_Pending_Is_Skipped_Not_The_Whole_Batch()
    {
        // A Unit with three controllers, one already holding an active command of this ActionType: that one is skipped, the other two still get created, outcome is still Success (not AllDuplicates).
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(7))
            .ReturnsAsync(new List<Device> { ControllerDevice(500), ControllerDevice(501), ControllerDevice(502) });
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(true);
        _commands.Setup(c => c.HasActiveCommandAsync(501, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.HasActiveCommandAsync(502, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(501, CommandActionType.Reboot, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(11);
        _commands.Setup(c => c.AddCommandAsync(502, CommandActionType.Reboot, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(12);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Unit, 7, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.Success, result.Outcome);
        Assert.Equal([11, 12], result.CreatedCommandIds);
        // Strict mock: AddCommandAsync(500, ...) was never set up - device 500 must not have been added.
    }

    [Fact]
    public async Task Unit_FanOut_Every_Controller_Already_Pending_Returns_AllDuplicates()
    {
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(7)).ReturnsAsync(new List<Device> { ControllerDevice(500), ControllerDevice(501) });
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(true);
        _commands.Setup(c => c.HasActiveCommandAsync(501, CommandActionType.Reboot, It.IsAny<DateTime>())).ReturnsAsync(true);

        var result = await NewService().IssueCommandAsync(CommandTargetType.Unit, 7, CommandActionType.Reboot);

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
    }


    [Fact]
    public async Task GetPendingCommand_Returns_Oldest_Pending_Command()
    {
        var older = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        var newer = new DeviceCommand { IDDeviceCommand = 2, DeviceID = 500, ActionType = CommandActionType.ForceOTA, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { older, newer }); // oldest first, per the repo's own contract

        PendingCommand? pending = await NewService().GetPendingCommandAsync(500);

        Assert.NotNull(pending);
        Assert.Equal(1, pending!.IDDeviceCommand);
    }

    [Fact]
    public async Task GetPendingCommand_Skips_And_Expires_Stale_Rows_Then_Returns_The_Next_Valid_One()
    {
        // A stuck expired Reboot must not hide a still-valid, newer ForceOTA command behind it.
        var expired = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        var valid = new DeviceCommand { IDDeviceCommand = 2, DeviceID = 500, ActionType = CommandActionType.ForceOTA, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { expired, valid });
        _commands.Setup(c => c.ExpirePendingCommandsAsync(500, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        PendingCommand? pending = await NewService().GetPendingCommandAsync(500);

        Assert.NotNull(pending);
        Assert.Equal(2, pending!.IDDeviceCommand);
        _commands.Verify(c => c.ExpirePendingCommandsAsync(500, It.IsAny<DateTime>()), Times.Once);
        // Strict mock: an un-set-up SetCommandStatusAsync call would throw, proving expiry is now one bulk call, not one write per expired row.
    }

    [Fact]
    public async Task GetPendingCommand_AllExpired_Returns_Null()
    {
        var expired = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { expired });
        _commands.Setup(c => c.ExpirePendingCommandsAsync(500, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        PendingCommand? pending = await NewService().GetPendingCommandAsync(500);

        Assert.Null(pending);
    }

    [Fact]
    public async Task GetPendingCommand_MultipleExpiredRows_ExpiresInOneBulkCall_NotOnePerRow()
    {
        var expired1 = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-10) };
        var expired2 = new DeviceCommand { IDDeviceCommand = 2, DeviceID = 500, ActionType = CommandActionType.ForceOTA, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        var valid = new DeviceCommand { IDDeviceCommand = 3, DeviceID = 500, ActionType = CommandActionType.ForceConfigSync, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { expired1, expired2, valid });
        _commands.Setup(c => c.ExpirePendingCommandsAsync(500, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        PendingCommand? pending = await NewService().GetPendingCommandAsync(500);

        Assert.Equal(3, pending!.IDDeviceCommand);
        _commands.Verify(c => c.ExpirePendingCommandsAsync(500, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingCommand_NoExpiredRows_NeverCallsExpire()
    {
        var valid = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { valid });
        // Strict mock: an un-set-up ExpirePendingCommandsAsync call would throw, proving it's skipped when nothing needs expiring.

        PendingCommand? pending = await NewService().GetPendingCommandAsync(500);

        Assert.Equal(1, pending!.IDDeviceCommand);
    }

    /// Two poll cycles: the first poll's ack+execute must not disturb the second, still-Pending command behind it - proves FIFO holds across cycles.
    [Fact]
    public async Task Two_Commands_Are_Acked_And_Executed_In_FIFO_Order_Across_Two_Poll_Cycles()
    {
        var first = new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };
        var second = new DeviceCommand { IDDeviceCommand = 2, DeviceID = 500, ActionType = CommandActionType.ForceOTA, Status = CommandStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(30) };

        // poll cycle 1: device sees, acks, and executes the first command
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { first, second });
        var service = NewService();
        PendingCommand? poll1 = await service.GetPendingCommandAsync(500);
        Assert.Equal(1, poll1!.IDDeviceCommand);

        _commands.Setup(c => c.GetCommandByIdAsync(1)).ReturnsAsync(first);
        _commands.Setup(c => c.SetCommandStatusAsync(1, CommandStatus.Acknowledged, null)).Returns(Task.CompletedTask);
        await service.AcknowledgeCommandAsync(1, 500);

        _commands.Setup(c => c.SetCommandStatusAsync(1, CommandStatus.Executed, It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        await service.MarkExecutedAsync(1, 500);

        // poll cycle 2: the first command is now Executed and gone from the pending list, the second surfaces next
        _commands.Setup(c => c.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand> { second });
        PendingCommand? poll2 = await service.GetPendingCommandAsync(500);
        Assert.Equal(2, poll2!.IDDeviceCommand);

        _commands.Verify(c => c.SetCommandStatusAsync(1, CommandStatus.Acknowledged, null), Times.Once);
        _commands.Verify(c => c.SetCommandStatusAsync(1, CommandStatus.Executed, It.IsAny<DateTime>()), Times.Once);
    }


    [Fact]
    public async Task Acknowledge_Ignores_A_Command_That_Is_No_Longer_Pending()
    {
        _commands.Setup(c => c.GetCommandByIdAsync(1))
            .ReturnsAsync(new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, Status = CommandStatus.Acknowledged });

        await NewService().AcknowledgeCommandAsync(1, 500);

        // Strict mock: SetCommandStatusAsync was never set up - a redundant ack must not call it.
    }

    [Fact]
    public async Task MarkExecuted_Ignores_An_Unknown_CommandId()
    {
        _commands.Setup(c => c.GetCommandByIdAsync(999)).ReturnsAsync((DeviceCommand?)null);

        await NewService().MarkExecutedAsync(999, 500);
    }

    [Fact]
    public async Task MarkExecuted_Accepts_Pending_Directly_Covering_Reboot_With_No_Prior_Ack()
    {
        // Reboot has nothing to ack-then-execute on the same connection - MarkExecutedAsync must accept a straight Pending -> Executed transition.
        _commands.Setup(c => c.GetCommandByIdAsync(1))
            .ReturnsAsync(new DeviceCommand { IDDeviceCommand = 1, DeviceID = 500, Status = CommandStatus.Pending });
        _commands.Setup(c => c.SetCommandStatusAsync(1, CommandStatus.Executed, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewService().MarkExecutedAsync(1, 500);

        _commands.Verify(c => c.SetCommandStatusAsync(1, CommandStatus.Executed, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Acknowledge_Ignores_A_Command_Belonging_To_A_Different_Device()
    {
        // device 500 must not be able to ack a Pending command that belongs to device 501.
        _commands.Setup(c => c.GetCommandByIdAsync(1))
            .ReturnsAsync(new DeviceCommand { IDDeviceCommand = 1, DeviceID = 501, Status = CommandStatus.Pending });

        await NewService().AcknowledgeCommandAsync(1, 500);

        // Strict mock: SetCommandStatusAsync was never set up - a cross-device ack must not call it.
    }

    [Fact]
    public async Task MarkExecuted_Ignores_A_Command_Belonging_To_A_Different_Device()
    {
        // device 500 must not be able to mark device 501's command Executed.
        _commands.Setup(c => c.GetCommandByIdAsync(1))
            .ReturnsAsync(new DeviceCommand { IDDeviceCommand = 1, DeviceID = 501, Status = CommandStatus.Pending });

        await NewService().MarkExecutedAsync(1, 500);

        // Strict mock: SetCommandStatusAsync was never set up - a cross-device execute confirmation must not call it.
    }

    [Fact]
    public async Task IssueWifiUpdate_Success_PublishesPayloadCarryingCommand()
    {
        DateTime before = DateTime.UtcNow;
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.UpdateWifiCredentials, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.UpdateWifiCredentials, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.Is<string?>(p => p != null && p.Contains("NewSsid") && p.Contains("NewPass")))).ReturnsAsync(9);
        _devices.Setup(d => d.DeviceGetByIdAsync(500)).ReturnsAsync(ControllerDevice(500));

        var result = await NewService().IssueWifiUpdateCommandAsync(500, "NewSsid", "NewPass");

        Assert.Equal(IssueCommandOutcome.Success, result.Outcome);
        Assert.Equal([9], result.CreatedCommandIds);
        _commands.Verify(c => c.AddCommandAsync(500, CommandActionType.UpdateWifiCredentials,
            It.Is<DateTime>(d => d >= before), It.Is<DateTime>(d => d > before), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task IssueWifiUpdate_AlreadyPendingForDevice_ReturnsAllDuplicates()
    {
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.UpdateWifiCredentials, It.IsAny<DateTime>())).ReturnsAsync(true);

        var result = await NewService().IssueWifiUpdateCommandAsync(500, "NewSsid", "NewPass");

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
        // Strict mock: AddCommandAsync was never set up - a call to it here would throw.
    }

    [Fact]
    public async Task IssueWifiUpdate_LosingTheDbLevelDedupRace_IsTreatedAsAllDuplicates_NotACrash()
    {
        _commands.Setup(c => c.HasActiveCommandAsync(500, CommandActionType.UpdateWifiCredentials, It.IsAny<DateTime>())).ReturnsAsync(false);
        _commands.Setup(c => c.AddCommandAsync(500, CommandActionType.UpdateWifiCredentials, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>()))
            .ReturnsAsync((int?)null);

        var result = await NewService().IssueWifiUpdateCommandAsync(500, "NewSsid", "NewPass");

        Assert.Equal(IssueCommandOutcome.AllDuplicates, result.Outcome);
        Assert.Empty(result.CreatedCommandIds);
    }

    private static string ProvisionPayloadJson(string discoveredApMac, string? deviceName = null, int? zoneId = null) =>
        System.Text.Json.JsonSerializer.Serialize(new DiscoveryProvisionPayload
        {
            Username = "admin@example.com",
            Pin = "ABC123",
            DiscoveredApMac = discoveredApMac,
            Ssid = "TestWifi",
            WifiPassword = "TestPass",
            DeviceName = deviceName,
            ZoneID = zoneId,
        });

    [Fact]
    public async Task ConsumePendingProvision_MatchingMac_ReturnsPayload_AndMarksExecuted()
    {
        var command = new DeviceCommand { IDDeviceCommand = 7, Status = CommandStatus.Acknowledged, Payload = ProvisionPayloadJson("AABBCCDDEEFF", "MySensor", 42) };
        _commands.Setup(c => c.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand> { command });
        _commands.Setup(c => c.SetCommandStatusAsync(7, CommandStatus.Executed, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        DiscoveryProvisionPayload? result = await NewService().ConsumePendingProvisionAsync("aabbccddeeff");

        Assert.NotNull(result);
        Assert.Equal("MySensor", result!.DeviceName);
        Assert.Equal(42, result.ZoneID);
    }

    [Fact]
    public async Task ConsumePendingProvision_NoMatchingMac_ReturnsNull()
    {
        var command = new DeviceCommand { IDDeviceCommand = 7, Status = CommandStatus.Acknowledged, Payload = ProvisionPayloadJson("112233445566") };
        _commands.Setup(c => c.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand> { command });

        DiscoveryProvisionPayload? result = await NewService().ConsumePendingProvisionAsync("AABBCCDDEEFF");

        Assert.Null(result);
        // Strict mock: SetCommandStatusAsync was never set up - a non-matching mac must not consume the command.
    }

    [Fact]
    public async Task ConsumePendingProvision_MalformedPayload_SkippedNotThrown()
    {
        var malformed = new DeviceCommand { IDDeviceCommand = 8, Status = CommandStatus.Pending, Payload = "not json" };
        _commands.Setup(c => c.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand> { malformed });

        DiscoveryProvisionPayload? result = await NewService().ConsumePendingProvisionAsync("AABBCCDDEEFF");

        Assert.Null(result);
    }
}
