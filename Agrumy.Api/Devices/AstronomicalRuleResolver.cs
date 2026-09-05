using System.Text.Json;
using api.Models;
using api.Utils;

namespace api.Devices
{
    /// Compiles every Astronomical condition inside a rule's Conditions list into an effective Schedule condition for today's local date so the firmware only ever has to understand ConditionType.Schedule; a rule with a condition that can't be resolved today (no location set, polar day/night, or a zero/negative window) is dropped entirely rather than sent with a broken link in its AND/OR chain, leaving the function's other rules intact.
    public static class AstronomicalRuleResolver
    {
        public static IList<DeviceUnitZoneRule> Resolve(IList<DeviceUnitZoneRule> rules, ServerConfig serverConfig, DateOnly localDate, int utcOffsetSeconds)
        {
            var result = new List<DeviceUnitZoneRule>(rules.Count);
            foreach (DeviceUnitZoneRule rule in rules)
            {
                if (!rule.Conditions.Any(c => c.ConditionType == ConditionType.Astronomical))
                {
                    result.Add(rule);
                    continue;
                }
                if (ResolveOne(rule, serverConfig, localDate, utcOffsetSeconds) is DeviceUnitZoneRule resolved)
                {
                    result.Add(resolved);
                }
            }
            return result;
        }

        private static DeviceUnitZoneRule? ResolveOne(DeviceUnitZoneRule rule, ServerConfig serverConfig, DateOnly localDate, int utcOffsetSeconds)
        {
            var resolvedConditions = new List<RuleCondition>(rule.Conditions.Count);
            foreach (RuleCondition condition in rule.Conditions)
            {
                if (condition.ConditionType != ConditionType.Astronomical)
                {
                    resolvedConditions.Add(condition);
                    continue;
                }
                if (ResolveCondition(condition, serverConfig, localDate, utcOffsetSeconds) is not RuleCondition resolvedCondition)
                {
                    return null;
                }
                resolvedConditions.Add(resolvedCondition);
            }

            return new DeviceUnitZoneRule
            {
                IDDeviceUnitZoneRule = rule.IDDeviceUnitZoneRule,
                TenantID = rule.TenantID,
                DeviceUnitID = rule.DeviceUnitID,
                DeviceUnitZoneID = rule.DeviceUnitZoneID,
                ActionType = rule.ActionType,
                RelayFunction = rule.RelayFunction,
                SensorMetric = rule.SensorMetric,
                Conditions = resolvedConditions,
                NotificationSubject = rule.NotificationSubject,
                NotificationBody = rule.NotificationBody,
            };
        }

        private static RuleCondition? ResolveCondition(RuleCondition condition, ServerConfig serverConfig, DateOnly localDate, int utcOffsetSeconds)
        {
            if (serverConfig.WeatherLocationLat is not double lat || serverConfig.WeatherLocationLon is not double lon)
            {
                return null;
            }
            AstronomicalConditionConfig? config = condition.ConditionConfig?.Deserialize<AstronomicalConditionConfig>(ConditionConfigJson.Options);
            if (config == null)
            {
                return null;
            }
            (int? sunrise, int? sunset) = SolarCalculator.Compute(localDate, lat, lon, utcOffsetSeconds);
            if (sunrise is not int sunriseSeconds || sunset is not int sunsetSeconds)
            {
                return null;
            }

            int start = Math.Clamp(sunriseSeconds + config.SunriseOffsetMinutes * 60, 0, 86399);
            int end = Math.Clamp(sunsetSeconds + config.SunsetOffsetMinutes * 60, 0, 86400);
            int duration = end - start;
            if (duration <= 0)
            {
                return null;
            }

            var schedule = new ScheduleConditionConfig(config.DaysOfWeek, start, duration);
            return condition with
            {
                ConditionType = ConditionType.Schedule,
                ConditionConfig = JsonSerializer.SerializeToNode(schedule, ConditionConfigJson.Options),
            };
        }
    }
}
