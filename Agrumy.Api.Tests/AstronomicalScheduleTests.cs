using System.Text.Json;
using api.Devices;
using api.Models;
using api.Utils;

namespace Agrumy.Api.Tests;

/// SolarCalculator's NOAA formulas (checked against well-known seasonal invariants, not hardcoded minute-precision tables) and AstronomicalRuleResolver's compile-to-Schedule step (#228).
public class AstronomicalScheduleTests
{
    [Fact]
    public void Compute_Equator_Equinox_DayLength_IsAbout12Hours()
    {
        var (sunrise, sunset) = SolarCalculator.Compute(new DateOnly(2026, 3, 20), latitude: 0, longitude: 0, utcOffsetSeconds: 0);
        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        int dayLengthSeconds = sunset!.Value - sunrise!.Value;
        Assert.InRange(dayLengthSeconds, 11 * 3600 + 45 * 60, 12 * 3600 + 15 * 60);
        // Solar noon near local 12:00 at longitude 0.
        Assert.InRange((sunrise.Value + sunset.Value) / 2, 11 * 3600, 13 * 3600);
    }

    [Fact]
    public void Compute_NorthernSummerSolstice_HighLatitude_DayIsMuchLongerThan12Hours()
    {
        var (sunrise, sunset) = SolarCalculator.Compute(new DateOnly(2026, 6, 21), latitude: 60, longitude: 0, utcOffsetSeconds: 0);
        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        Assert.True(sunset!.Value - sunrise!.Value > 17 * 3600);
    }

    [Fact]
    public void Compute_SouthernSummerSolstice_MidLatitude_DayIsLongerThan12Hours()
    {
        // December is southern-hemisphere summer - a mid-southern latitude should see a long day, mirroring the northern June case above.
        var (sunrise, sunset) = SolarCalculator.Compute(new DateOnly(2026, 12, 21), latitude: -45, longitude: 0, utcOffsetSeconds: 0);
        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        Assert.True(sunset!.Value - sunrise!.Value > 14 * 3600);
    }

    [Fact]
    public void Compute_PolarNight_ReturnsNull()
    {
        var (sunrise, sunset) = SolarCalculator.Compute(new DateOnly(2026, 12, 21), latitude: 80, longitude: 0, utcOffsetSeconds: 0);
        Assert.Null(sunrise);
        Assert.Null(sunset);
    }

    [Fact]
    public void Resolve_NoLocationConfigured_DropsRule()
    {
        var rules = new List<DeviceUnitZoneRule> { AstroRule(daysOfWeek: 127, sunriseOffset: 0, sunsetOffset: 0) };
        var serverConfig = new ServerConfig();

        IList<DeviceUnitZoneRule> resolved = AstronomicalRuleResolver.Resolve(rules, serverConfig, new DateOnly(2026, 6, 21), utcOffsetSeconds: 0);

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_WithLocation_CompilesToScheduleWindowSpanningSunriseToSunset()
    {
        var rules = new List<DeviceUnitZoneRule> { AstroRule(daysOfWeek: 127, sunriseOffset: -30, sunsetOffset: 60) };
        var serverConfig = new ServerConfig { WeatherLocationLat = 45.8, WeatherLocationLon = 16.0 };

        IList<DeviceUnitZoneRule> resolved = AstronomicalRuleResolver.Resolve(rules, serverConfig, new DateOnly(2026, 6, 21), utcOffsetSeconds: 7200);

        DeviceUnitZoneRule rule = Assert.Single(resolved);
        RuleCondition condition = Assert.Single(rule.Conditions);
        Assert.Equal(ConditionType.Schedule, condition.ConditionType);
        var schedule = condition.ConditionConfig!.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options)!;
        Assert.Equal(127, schedule.DaysOfWeek);

        var (sunrise, sunset) = SolarCalculator.Compute(new DateOnly(2026, 6, 21), 45.8, 16.0, 7200);
        Assert.Equal(Math.Clamp(sunrise!.Value - 30 * 60, 0, 86399), schedule.Start);
        Assert.Equal(Math.Clamp(sunset!.Value + 60 * 60, 0, 86400) - schedule.Start, schedule.Duration);
    }

    [Fact]
    public void Resolve_OffsetsCollapseWindowToZero_DropsRule()
    {
        // Sunrise offset pushed past the sunset offset on the same day leaves nothing "on".
        var rules = new List<DeviceUnitZoneRule> { AstroRule(daysOfWeek: 127, sunriseOffset: 700, sunsetOffset: -700) };
        var serverConfig = new ServerConfig { WeatherLocationLat = 45.8, WeatherLocationLon = 16.0 };

        IList<DeviceUnitZoneRule> resolved = AstronomicalRuleResolver.Resolve(rules, serverConfig, new DateOnly(2026, 6, 21), utcOffsetSeconds: 7200);

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_NonAstronomicalRule_PassesThroughUnchanged()
    {
        var scheduleRule = new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = 1,
            RelayFunction = RelayFunction.Light,
            Conditions = [new RuleCondition(ConditionType.Schedule, JsonSerializer.SerializeToNode(new ScheduleConditionConfig(127, 0, 3600), ConditionConfigJson.Options), null)],
        };

        IList<DeviceUnitZoneRule> resolved = AstronomicalRuleResolver.Resolve([scheduleRule], new ServerConfig(), new DateOnly(2026, 6, 21), 0);

        Assert.Same(scheduleRule, Assert.Single(resolved));
    }

    private static DeviceUnitZoneRule AstroRule(int daysOfWeek, int sunriseOffset, int sunsetOffset) => new()
    {
        DeviceUnitZoneID = 1,
        RelayFunction = RelayFunction.Light,
        Conditions = [new RuleCondition(ConditionType.Astronomical, JsonSerializer.SerializeToNode(new AstronomicalConditionConfig(daysOfWeek, sunriseOffset, sunsetOffset), ConditionConfigJson.Options), null)],
    };
}
