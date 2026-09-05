using System.Text.Json;
using api.Models;

namespace api.Devices
{
    /// Server-side evaluation of one Notification-action rule's Conditions fold, mirroring AgrumyFirmware's
    /// RelayLogic.cpp/ActuatorController semantics exactly (same Threshold dead-zone math, same grid-aligned
    /// Interval formula, same Schedule window check, same strict left-to-right AND/OR fold) - reimplemented
    /// in C# rather than shared, since a Relay rule's fold runs on-device and a Notification rule's runs here.
    public static class RuleConditionEvaluator
    {
        /// wasRuleTrue is the rule's own last-known folded result (RuleNotificationState.WasTrue) - used as
        /// EVERY Threshold condition's dead-zone latch input, since there is no per-condition state to store.
        /// This is an approximation for a rule with more than one Threshold condition (each shares the whole
        /// rule's latch rather than having its own), traded for not needing a per-condition state table.
        public static bool EvaluateRule(DeviceUnitZoneRule rule, bool wasRuleTrue, double? metricReading,
            DateTime utcNow, int utcOffsetSeconds, Func<int, bool> referencedRuleFiredThisTick)
        {
            if (rule.Conditions.Count == 0)
            {
                return false;
            }
            bool result = EvaluateCondition(rule.Conditions[0], wasRuleTrue, metricReading, utcNow, utcOffsetSeconds, referencedRuleFiredThisTick);
            for (int i = 1; i < rule.Conditions.Count; i++)
            {
                bool next = EvaluateCondition(rule.Conditions[i], wasRuleTrue, metricReading, utcNow, utcOffsetSeconds, referencedRuleFiredThisTick);
                result = rule.Conditions[i].Operator == LogicalOperator.And ? (result && next) : (result || next);
            }
            return result;
        }

        private static bool EvaluateCondition(RuleCondition condition, bool wasRuleTrue, double? metricReading,
            DateTime utcNow, int utcOffsetSeconds, Func<int, bool> referencedRuleFiredThisTick)
        {
            switch (condition.ConditionType)
            {
                case ConditionType.Threshold:
                {
                    var config = condition.ConditionConfig?.Deserialize<ThresholdConditionConfig>(ConditionConfigJson.Options);
                    if (config == null || metricReading is not double reading || double.IsNaN(reading))
                    {
                        return false;
                    }
                    // Notification threshold direction is always "turns on above" (api.Models.ThresholdConditionConfig) - unlike a Relay rule, there's no RelayFunction to imply a different direction.
                    return ComputeThresholdState(wasRuleTrue, reading, config.Threshold, config.Hysteresis, turnsOnAboveThreshold: true);
                }
                case ConditionType.Interval:
                {
                    var config = condition.ConditionConfig?.Deserialize<IntervalConditionConfig>(ConditionConfigJson.Options);
                    return config != null && config.Interval > 0 && ComputeIntervalState(config.Interval, config.IntervalLength, utcNow);
                }
                case ConditionType.Schedule:
                {
                    var config = condition.ConditionConfig?.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options);
                    if (config == null)
                    {
                        return false;
                    }
                    DateTime local = utcNow.AddSeconds(utcOffsetSeconds);
                    int localWeekday = (int)local.DayOfWeek; // 0=Sunday..6=Saturday, matches the 7-bit mask convention
                    int localSecondsOfDay = local.Hour * 3600 + local.Minute * 60 + local.Second;
                    return ComputeScheduleState(config.DaysOfWeek, config.Start, config.Duration, localWeekday, localSecondsOfDay);
                }
                case ConditionType.RuleTriggered:
                {
                    var config = condition.ConditionConfig?.Deserialize<RuleTriggeredConditionConfig>(ConditionConfigJson.Options);
                    return config != null && referencedRuleFiredThisTick(config.ReferencedRuleId);
                }
                default:
                    return false;
            }
        }

        // ---- Pure math, mirrors AgrumyFirmware's RelayLogic.cpp exactly ---------------------------------

        private static bool ComputeThresholdState(bool currentlyOn, double reading, double threshold, double hysteresis, bool turnsOnAboveThreshold)
        {
            if (turnsOnAboveThreshold)
            {
                if (reading > threshold + hysteresis) { return true; }
                if (reading <= threshold) { return false; }
            }
            else
            {
                if (reading < threshold) { return true; }
                if (reading >= threshold + hysteresis) { return false; }
            }
            return currentlyOn; // dead zone - neither condition met, state latches
        }

        private static bool ComputeIntervalState(int interval, int intervalLength, DateTime utcNow)
        {
            long epochSeconds = ((DateTimeOffset)DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long positionInCycle = epochSeconds % interval;
            return positionInCycle < intervalLength;
        }

        private static bool ComputeScheduleState(int daysOfWeekMask, int startSeconds, int durationSeconds, int localWeekday, int localSecondsOfDay)
        {
            bool todayIsScheduled = (daysOfWeekMask & (1 << localWeekday)) != 0;
            return todayIsScheduled && localSecondsOfDay >= startSeconds && localSecondsOfDay < startSeconds + durationSeconds;
        }
    }
}
