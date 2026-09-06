using api.Commands;
using api.Dal.Interface;
using api.Models;
using Moq;

namespace Agrumy.Api.Tests;

/// Exercises ManualActuateService directly - no HTTP/controller plumbing, no database (IDeviceFarmUnitRepository is mocked).
public class ManualActuateServiceTests
{
    private readonly Mock<IDeviceFarmUnitRepository> _units = new(MockBehavior.Strict);

    private ManualActuateService NewService() => new(_units.Object);

    private static Device ControllerDevice(int id, int idZone) => new() { IDDevice = id, DeviceFarmUnitZoneID = idZone, DeviceControllerEnabled = true };

    [Fact]
    public async Task Zone_With_No_Controller_Returns_TargetNotFound()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync((Device?)null);

        var result = await NewService().StartForZoneAsync(10, new ManualActuateRequest(RelayFunction.Heating, ManualOverrideMode.Duration, 300, null, null, null));

        Assert.Equal(ManualActuateOutcome.TargetNotFound, result.Outcome);
    }

    [Fact]
    public async Task Light_Cannot_Be_Manually_Actuated()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7 });

        var result = await NewService().StartForZoneAsync(10, new ManualActuateRequest(RelayFunction.Light, ManualOverrideMode.Duration, 300, null, null, null));

        Assert.Equal(ManualActuateOutcome.UnsupportedFunction, result.Outcome);
    }

    [Fact]
    public async Task Target_Mode_Wrong_Metric_For_Function_Returns_InvalidTargetMetric()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, HeatingMaxRunSeconds = 1800 });

        // Heating only accepts Temperature, not Humidity.
        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.Heating, ManualOverrideMode.Target, null, SensorMetric.Humidity, 20.0, 2.0));

        Assert.Equal(ManualActuateOutcome.InvalidTargetMetric, result.Outcome);
    }

    [Fact]
    public async Task Target_Mode_Without_MaxRunSeconds_Configured_Is_Refused()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        // No HeatingMaxRunSeconds set - Target mode has no natural self-cap, must not be allowed to run forever.
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, HeatingMaxRunSeconds = null });

        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.Heating, ManualOverrideMode.Target, null, SensorMetric.Temperature, 22.0, 1.0));

        Assert.Equal(ManualActuateOutcome.MissingMaxRunSeconds, result.Outcome);
    }

    [Fact]
    public async Task Duration_Mode_Requested_Below_MaxRunSeconds_Uses_Requested_Duration()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, WaterPumpMaxRunSeconds = 1800 });

        DateTime before = DateTime.UtcNow;
        DeviceManualOverride? captured = null;
        _units.Setup(u => u.ManualOverrideStartAsync(It.IsAny<DeviceManualOverride>()))
              .Callback<DeviceManualOverride>(o => captured = o)
              .Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.WaterPump, ManualOverrideMode.Duration, 300, null, null, null));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.Equal([1], result.AffectedDeviceIds);
        Assert.NotNull(captured);
        Assert.True((captured!.ExpiresAtUtc - before).TotalSeconds is >= 299 and <= 301);
    }

    [Fact]
    public async Task Duration_Mode_Requested_Above_MaxRunSeconds_Is_Capped()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, WaterPumpMaxRunSeconds = 60 });

        DateTime before = DateTime.UtcNow;
        DeviceManualOverride? captured = null;
        _units.Setup(u => u.ManualOverrideStartAsync(It.IsAny<DeviceManualOverride>()))
              .Callback<DeviceManualOverride>(o => captured = o)
              .Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        // Requested 2 hours, but the zone's own safety cap is 60s - the cap must win.
        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.WaterPump, ManualOverrideMode.Duration, 7200, null, null, null));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.NotNull(captured);
        Assert.True((captured!.ExpiresAtUtc - before).TotalSeconds is >= 59 and <= 61);
    }

    [Fact]
    public async Task Duration_Mode_With_No_MaxRunSeconds_Configured_Uses_Requested_Duration_Unclamped()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        // No zone-level cap at all - Duration mode is still safe since it has its own explicit stop time.
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, WaterPumpMaxRunSeconds = null });

        DateTime before = DateTime.UtcNow;
        DeviceManualOverride? captured = null;
        _units.Setup(u => u.ManualOverrideStartAsync(It.IsAny<DeviceManualOverride>()))
              .Callback<DeviceManualOverride>(o => captured = o)
              .Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.WaterPump, ManualOverrideMode.Duration, 900, null, null, null));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.NotNull(captured);
        Assert.True((captured!.ExpiresAtUtc - before).TotalSeconds is >= 899 and <= 901);
    }

    [Fact]
    public async Task Target_Mode_Success_Carries_Metric_Threshold_Hysteresis_Through()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, VentilationMaxRunSeconds = 1800 });

        DeviceManualOverride? captured = null;
        _units.Setup(u => u.ManualOverrideStartAsync(It.IsAny<DeviceManualOverride>()))
              .Callback<DeviceManualOverride>(o => captured = o)
              .Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        var result = await NewService().StartForZoneAsync(10,
            new ManualActuateRequest(RelayFunction.Ventilation, ManualOverrideMode.Target, null, SensorMetric.Humidity, 65.0, 5.0));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(ManualOverrideMode.Target, captured!.Mode);
        Assert.Equal(SensorMetric.Humidity, captured.TargetMetric);
        Assert.Equal(65.0, captured.TargetThreshold);
        Assert.Equal(5.0, captured.TargetHysteresis);
        Assert.Equal(7, captured.TenantID);
    }

    [Fact]
    public async Task Unit_FanOut_Skips_Zones_With_No_Controller()
    {
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(3)).ReturnsAsync(new List<Device>
        {
            ControllerDevice(1, 10),
            ControllerDevice(2, 11),
        });
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, WaterPumpMaxRunSeconds = 1800 });
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(11)).ReturnsAsync((DeviceFarmUnitZone?)null); // deleted zone race, defensively skipped

        _units.Setup(u => u.ManualOverrideStartAsync(It.Is<DeviceManualOverride>(o => o.DeviceID == 1))).Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        var result = await NewService().StartForUnitAsync(3,
            new ManualActuateRequest(RelayFunction.WaterPump, ManualOverrideMode.Duration, 300, null, null, null));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.Equal([1], result.AffectedDeviceIds);
        // Strict mock: ManualOverrideStartAsync/DeviceFarmUnitZoneConfigVersionBumpAsync for device 2/zone 11 would throw if called - only device 1 should have been targeted.
    }

    [Fact]
    public async Task Unit_FanOut_Target_Mode_One_Zone_Missing_MaxRunSeconds_Still_Starts_The_Other_And_Bumps_It()
    {
        // Regression: an earlier version returned early on the first zone lacking MaxRunSeconds, silently
        // leaving an already-started override on a PRECEDING zone without its ConfigVersion bump.
        _units.Setup(u => u.DeviceFarmUnitGetControllersAsync(3)).ReturnsAsync(new List<Device>
        {
            ControllerDevice(1, 10), // has HeatingMaxRunSeconds configured
            ControllerDevice(2, 11), // does not
        });
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(10)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 10, TenantID = 7, HeatingMaxRunSeconds = 1800 });
        _units.Setup(u => u.DeviceFarmUnitZoneGetByIdAsync(11)).ReturnsAsync(new DeviceFarmUnitZone { IDDeviceFarmUnitZone = 11, TenantID = 7, HeatingMaxRunSeconds = null });

        _units.Setup(u => u.ManualOverrideStartAsync(It.Is<DeviceManualOverride>(o => o.DeviceID == 1))).Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        var result = await NewService().StartForUnitAsync(3,
            new ManualActuateRequest(RelayFunction.Heating, ManualOverrideMode.Target, null, SensorMetric.Temperature, 22.0, 1.0));

        Assert.Equal(ManualActuateOutcome.Success, result.Outcome);
        Assert.Equal([1], result.AffectedDeviceIds);
        Assert.NotNull(result.Message); // partial-success note
        // Strict mock: ManualOverrideStartAsync/DeviceFarmUnitZoneConfigVersionBumpAsync for device 2/zone 11 would throw if called.
    }

    [Fact]
    public async Task Stop_With_No_Controller_Is_A_NoOp()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync((Device?)null);

        await NewService().StopAsync(10, RelayFunction.WaterPump);

        // Strict mock: ManualOverrideStopAsync/DeviceFarmUnitZoneConfigVersionBumpAsync would throw if called.
    }

    [Fact]
    public async Task Stop_With_Controller_Stops_And_Bumps_ConfigVersion()
    {
        _units.Setup(u => u.DeviceFarmUnitZoneGetControllerAsync(10)).ReturnsAsync(ControllerDevice(1, 10));
        _units.Setup(u => u.ManualOverrideStopAsync(1, RelayFunction.WaterPump)).Returns(Task.CompletedTask);
        _units.Setup(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10)).Returns(Task.CompletedTask);

        await NewService().StopAsync(10, RelayFunction.WaterPump);

        _units.Verify(u => u.ManualOverrideStopAsync(1, RelayFunction.WaterPump), Times.Once);
        _units.Verify(u => u.DeviceFarmUnitZoneConfigVersionBumpAsync(10), Times.Once);
    }
}
