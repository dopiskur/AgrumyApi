using System.Text.Json;
using api.Devices;
using api.Models;

namespace Agrumy.Api.Tests;

/// Zone>Unit>Global precedence (roadmap #212) - a scope's rules for a function/metric fully replace, never merge with, a less specific scope's.
public class RuleHierarchyResolverTests
{
    private static DeviceUnitZoneRule RelayRule(RelayFunction function, int marker, int? zoneId = null, int? unitId = null) => new()
    {
        IDDeviceUnitZoneRule = marker,
        TenantID = 1,
        DeviceUnitZoneID = zoneId,
        DeviceUnitID = unitId,
        ActionType = ActionType.Relay,
        RelayFunction = function,
        Conditions = [new RuleCondition(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(1, 1), ConditionConfigJson.Options), null)],
    };

    [Fact]
    public void ResolveRelayRules_ZoneRuleExists_ZoneWinsOverUnitAndGlobal()
    {
        var zoneRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Light, 1, zoneId: 5) };
        var unitRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Light, 2, unitId: 9) };
        var globalRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Light, 3) };

        var result = RuleHierarchyResolver.ResolveRelayRules(zoneRules, unitRules, globalRules);

        Assert.Equal([1], result.Select(r => r.IDDeviceUnitZoneRule));
    }

    [Fact]
    public void ResolveRelayRules_NoZoneRule_FallsBackToUnit()
    {
        var unitRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Heating, 2, unitId: 9) };
        var globalRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Heating, 3) };

        var result = RuleHierarchyResolver.ResolveRelayRules([], unitRules, globalRules);

        Assert.Equal([2], result.Select(r => r.IDDeviceUnitZoneRule));
    }

    [Fact]
    public void ResolveRelayRules_NoZoneOrUnitRule_FallsBackToGlobal()
    {
        var globalRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.WaterPump, 3) };

        var result = RuleHierarchyResolver.ResolveRelayRules([], [], globalRules);

        Assert.Equal([3], result.Select(r => r.IDDeviceUnitZoneRule));
    }

    [Fact]
    public void ResolveRelayRules_DifferentFunctionsResolveIndependently()
    {
        // Zone defines Light, but not Heating - Heating should still fall through to Global, Light stays Zone's own.
        var zoneRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Light, 1, zoneId: 5) };
        var globalRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Heating, 3), RelayRule(RelayFunction.Light, 4) };

        var result = RuleHierarchyResolver.ResolveRelayRules(zoneRules, [], globalRules);

        Assert.Equal([1, 3], result.Select(r => r.IDDeviceUnitZoneRule).OrderBy(x => x));
    }

    [Fact]
    public void ResolveRelayRules_MultipleZoneRulesSameFunction_AllSurvive_OrSemanticsPreserved()
    {
        var zoneRules = new List<DeviceUnitZoneRule> { RelayRule(RelayFunction.Ventilation, 1, zoneId: 5), RelayRule(RelayFunction.Ventilation, 2, zoneId: 5) };

        var result = RuleHierarchyResolver.ResolveRelayRules(zoneRules, [], []);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveNotificationRules_NullMetric_IsItsOwnGroup()
    {
        var zoneRule = new DeviceUnitZoneRule
        {
            IDDeviceUnitZoneRule = 1, TenantID = 1, DeviceUnitZoneID = 5, ActionType = ActionType.Notification, SensorMetric = null,
            Conditions = [new RuleCondition(ConditionType.Schedule, JsonSerializer.SerializeToNode(new ScheduleConditionConfig(127, 0, 60), ConditionConfigJson.Options), null)],
            NotificationSubject = "reminder",
        };
        var globalRule = new DeviceUnitZoneRule
        {
            IDDeviceUnitZoneRule = 2, TenantID = 1, ActionType = ActionType.Notification, SensorMetric = SensorMetric.Temperature,
            Conditions = [new RuleCondition(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(30, 1), ConditionConfigJson.Options), null)],
            NotificationSubject = "hot",
        };

        var result = RuleHierarchyResolver.ResolveNotificationRules([zoneRule], [], [globalRule]);

        // Both survive - null-metric zone rule and Temperature-metric global rule are independent groups, neither shadows the other.
        Assert.Equal([1, 2], result.Select(r => r.IDDeviceUnitZoneRule).OrderBy(x => x));
    }
}
