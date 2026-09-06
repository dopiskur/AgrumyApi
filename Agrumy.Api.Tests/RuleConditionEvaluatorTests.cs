using System.Text.Json;
using api.Devices;
using api.Models;

namespace Agrumy.Api.Tests;

/// Server-side Notification-action rule evaluation (roadmap #212) - mirrors AgrumyFirmware's RelayLogic.cpp semantics for the same condition types.
public class RuleConditionEvaluatorTests
{
    private static DeviceUnitZoneRule Rule(params RuleCondition[] conditions) => new()
    {
        IDDeviceUnitZoneRule = 1,
        TenantID = 1,
        ActionType = ActionType.Notification,
        SensorMetric = SensorMetric.Temperature,
        Conditions = conditions,
        NotificationSubject = "test",
    };

    private static RuleCondition Threshold(double threshold, double hysteresis, LogicalOperator? op = null) =>
        new(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(threshold, hysteresis), ConditionConfigJson.Options), op);

    [Fact]
    public void Threshold_ReadingAboveThreshold_TurnsOn()
    {
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(Threshold(30, 2)), wasRuleTrue: false, metricReading: 33, DateTime.UtcNow, 0, _ => false);
        Assert.True(result);
    }

    [Fact]
    public void Threshold_ReadingAtThreshold_StaysOff()
    {
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(Threshold(30, 2)), wasRuleTrue: false, metricReading: 30, DateTime.UtcNow, 0, _ => false);
        Assert.False(result);
    }

    [Fact]
    public void Threshold_DeadZone_LatchesOnPreviousRuleState()
    {
        // Notification threshold direction is turnsOnAboveThreshold=true, so the dead zone sits BELOW threshold(30): (threshold-hysteresis, threshold] = (28, 30]. wasRuleTrue is the ONLY state available (no per-condition storage), so it drives the latch here.
        bool stillOn = RuleConditionEvaluator.EvaluateRule(Rule(Threshold(30, 2)), wasRuleTrue: true, metricReading: 29, DateTime.UtcNow, 0, _ => false);
        bool staysOff = RuleConditionEvaluator.EvaluateRule(Rule(Threshold(30, 2)), wasRuleTrue: false, metricReading: 29, DateTime.UtcNow, 0, _ => false);
        Assert.True(stillOn);
        Assert.False(staysOff);
    }

    [Fact]
    public void Threshold_NoReading_IsFalse()
    {
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(Threshold(30, 2)), wasRuleTrue: true, metricReading: null, DateTime.UtcNow, 0, _ => false);
        Assert.False(result);
    }

    [Fact]
    public void Interval_WithinOnWindow_IsTrue()
    {
        var condition = new RuleCondition(ConditionType.Interval, JsonSerializer.SerializeToNode(new IntervalConditionConfig(3600, 60), ConditionConfigJson.Options), null);
        // Epoch 0 (1970-01-01T00:00:00Z) is grid-aligned to the start of every interval - position-in-cycle 0, within the first 60s.
        var epochZero = DateTimeOffset.FromUnixTimeSeconds(0).UtcDateTime;
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, epochZero, 0, _ => false);
        Assert.True(result);
    }

    [Fact]
    public void Interval_OutsideOnWindow_IsFalse()
    {
        var condition = new RuleCondition(ConditionType.Interval, JsonSerializer.SerializeToNode(new IntervalConditionConfig(3600, 60), ConditionConfigJson.Options), null);
        var midCycle = DateTimeOffset.FromUnixTimeSeconds(1800).UtcDateTime; // 30 minutes into a 60-minute cycle, well past the 60s on-window
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, midCycle, 0, _ => false);
        Assert.False(result);
    }

    [Fact]
    public void Schedule_WithinWindowOnScheduledDay_IsTrue()
    {
        // 2026-09-06 is a Sunday (bit 0). Window 08:00-09:00 local, checked at 08:30 UTC with 0 offset.
        var condition = new RuleCondition(ConditionType.Schedule, JsonSerializer.SerializeToNode(new ScheduleConditionConfig(0b1, 8 * 3600, 3600), ConditionConfigJson.Options), null);
        var sundayMorning = new DateTime(2026, 9, 6, 8, 30, 0, DateTimeKind.Utc);
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, sundayMorning, 0, _ => false);
        Assert.True(result);
    }

    [Fact]
    public void Schedule_WrongDay_IsFalse()
    {
        var condition = new RuleCondition(ConditionType.Schedule, JsonSerializer.SerializeToNode(new ScheduleConditionConfig(0b1, 8 * 3600, 3600), ConditionConfigJson.Options), null); // Sunday only
        var mondayMorning = new DateTime(2026, 9, 7, 8, 30, 0, DateTimeKind.Utc);
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, mondayMorning, 0, _ => false);
        Assert.False(result);
    }

    [Fact]
    public void RuleTriggered_ReferencedRuleFiredThisTick_IsTrue()
    {
        var condition = new RuleCondition(ConditionType.RuleTriggered, JsonSerializer.SerializeToNode(new RuleTriggeredConditionConfig(42), ConditionConfigJson.Options), null);
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, DateTime.UtcNow, 0, id => id == 42);
        Assert.True(result);
    }

    [Fact]
    public void RuleTriggered_ReferencedRuleDidNotFire_IsFalse()
    {
        var condition = new RuleCondition(ConditionType.RuleTriggered, JsonSerializer.SerializeToNode(new RuleTriggeredConditionConfig(42), ConditionConfigJson.Options), null);
        bool result = RuleConditionEvaluator.EvaluateRule(Rule(condition), wasRuleTrue: false, metricReading: null, DateTime.UtcNow, 0, id => id == 99);
        Assert.False(result);
    }

    [Fact]
    public void Fold_ThreeConditions_StrictLeftToRight_NotOperatorPrecedence()
    {
        // (false AND true) OR true = true - a precedence-aware evaluator (AND binds tighter) would instead compute false AND (true OR true) = false.
        var rule = Rule(
            Threshold(1000, 0), // reading 5 -> false
            Threshold(-1000, 0, LogicalOperator.And), // reading 5 -> true (well above), ANDed with previous false
            Threshold(-1000, 0, LogicalOperator.Or)); // true, OR'd with the (false AND true) = false result
        bool result = RuleConditionEvaluator.EvaluateRule(rule, wasRuleTrue: false, metricReading: 5, DateTime.UtcNow, 0, _ => false);
        Assert.True(result);
    }
}
