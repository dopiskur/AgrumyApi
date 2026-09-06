using System.Text.Json;
using api.Models;
using api.Simulation;

namespace api.Devices
{
    /// Decides a simulated device's relay on/off state from the SAME rules a real device would receive over /api/Device/Config, mirroring AgrumyFirmware's ActuatorController::evaluateRule exactly (same per-function metric/direction, same OR-across-rules, same left-to-right AND/OR fold) - reimplemented rather than shared with api.Devices.RuleConditionEvaluator, same reasoning as that class's own comment (a third independent axis: on-device, server-side Notification, and now simulated).
    public static class SimulatedRelayEvaluator
    {
        /// wasOn is the relay's own last-known state (for Threshold's dead-zone latch, same rule as RuleConditionEvaluator's wasRuleTrue) - several rules for the same function OR together, unchanged from AgrumyFirmware's OR-across-rules semantics.
        public static bool Evaluate(RelayFunction function, IList<DeviceFarmUnitZoneRule> rules, bool wasOn, SimulatedReading reading, DateTime utcNow, int utcOffsetSeconds)
        {
            bool any = false;
            foreach (DeviceFarmUnitZoneRule rule in rules)
            {
                if (rule.ActionType != ActionType.Relay || rule.RelayFunction != function || rule.Conditions.Count == 0)
                {
                    continue;
                }
                any |= EvaluateRule(function, rule, wasOn, reading, utcNow, utcOffsetSeconds);
            }
            return any;
        }

        private static bool EvaluateRule(RelayFunction function, DeviceFarmUnitZoneRule rule, bool wasOn, SimulatedReading reading, DateTime utcNow, int utcOffsetSeconds)
        {
            bool result = EvaluateCondition(function, rule.Conditions[0], wasOn, reading, utcNow, utcOffsetSeconds);
            for (int i = 1; i < rule.Conditions.Count; i++)
            {
                bool next = EvaluateCondition(function, rule.Conditions[i], wasOn, reading, utcNow, utcOffsetSeconds);
                result = rule.Conditions[i].Operator == LogicalOperator.And ? (result && next) : (result || next);
            }
            return result;
        }

        // Same metric/direction table as AgrumyFirmware's ActuatorController::evaluateCondition (CONDITION_THRESHOLD case).
        private static bool EvaluateCondition(RelayFunction function, RuleCondition condition, bool wasOn, SimulatedReading reading, DateTime utcNow, int utcOffsetSeconds)
        {
            switch (condition.ConditionType)
            {
                case ConditionType.Threshold:
                {
                    var config = condition.ConditionConfig?.Deserialize<ThresholdConditionConfig>(ConditionConfigJson.Options);
                    (double value, bool turnsOnAbove) = function switch
                    {
                        RelayFunction.Ventilation => (reading.Humidity, true),
                        RelayFunction.Light => ((double)reading.Light, false),
                        RelayFunction.Heating => (reading.Temperature, false),
                        RelayFunction.WaterPump => ((double)reading.WaterLevel, false),
                        _ => (0.0, false),
                    };
                    return config != null
                        && RuleConditionEvaluator.ComputeThresholdState(wasOn, value, config.Threshold, config.Hysteresis, turnsOnAbove);
                }
                case ConditionType.Interval:
                {
                    var config = condition.ConditionConfig?.Deserialize<IntervalConditionConfig>(ConditionConfigJson.Options);
                    return config != null && config.Interval > 0 && RuleConditionEvaluator.ComputeIntervalState(config.Interval, config.IntervalLength, utcNow);
                }
                case ConditionType.Schedule:
                {
                    var config = condition.ConditionConfig?.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options);
                    if (config == null)
                    {
                        return false;
                    }
                    DateTime local = utcNow.AddSeconds(utcOffsetSeconds);
                    return RuleConditionEvaluator.ComputeScheduleState(config.DaysOfWeek, config.Start, config.Duration, (int)local.DayOfWeek, local.Hour * 3600 + local.Minute * 60 + local.Second);
                }
                default:
                    return false; // RuleTriggered never reaches a Relay-action rule's wire shape (Notification-only), same as RuleConditionEvaluator's default case.
            }
        }
    }
}
