using System.Text.Json;
using api.Models;
using api.Utils;

namespace api.Devices
{
    /// Compiles each Astronomical rule into an effective Schedule rule for today's local date so the firmware only ever has to understand ConditionType.Schedule; a rule that can't be resolved today (no location set, polar day/night, or a zero/negative window) is dropped rather than sent broken, leaving the function's other rules intact.
    public static class AstronomicalRuleResolver
    {
        public static IList<DeviceUnitZoneRule> Resolve(IList<DeviceUnitZoneRule> rules, ServerConfig serverConfig, DateOnly localDate, int utcOffsetSeconds)
        {
            var result = new List<DeviceUnitZoneRule>(rules.Count);
            foreach (DeviceUnitZoneRule rule in rules)
            {
                if (rule.ConditionType != ConditionType.Astronomical)
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
            if (serverConfig.WeatherLocationLat is not double lat || serverConfig.WeatherLocationLon is not double lon)
            {
                return null;
            }
            AstronomicalConditionConfig? config = rule.ConditionConfig?.Deserialize<AstronomicalConditionConfig>(ConditionConfigJson.Options);
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
            return new DeviceUnitZoneRule
            {
                IDDeviceUnitZoneRule = rule.IDDeviceUnitZoneRule,
                DeviceUnitZoneID = rule.DeviceUnitZoneID,
                RelayFunction = rule.RelayFunction,
                ConditionType = ConditionType.Schedule,
                ConditionConfig = JsonSerializer.SerializeToNode(schedule, ConditionConfigJson.Options),
            };
        }
    }
}
