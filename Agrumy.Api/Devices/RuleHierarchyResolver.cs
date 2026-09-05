using api.Models;

namespace api.Devices
{
    /// Resolves the CSS-cascade-style Zone>Unit>Global(per-tenant) rule precedence for one zone's rules - a scope's rules for a function/metric fully replace (not merge with) a less specific scope's, they never combine. ResolveRelayRules groups by RelayFunction (called from DeviceConfigBuilder, output goes to firmware); ResolveNotificationRules groups by SensorMetric (called from api.BackgroundWorkers.RuleNotificationEvaluator, server-side only).
    public static class RuleHierarchyResolver
    {
        public static IList<DeviceUnitZoneRule> ResolveRelayRules(IList<DeviceUnitZoneRule> zoneRules, IList<DeviceUnitZoneRule> unitRules, IList<DeviceUnitZoneRule> globalRules)
        {
            var result = new List<DeviceUnitZoneRule>();
            foreach (RelayFunction function in Enum.GetValues<RelayFunction>())
            {
                IList<DeviceUnitZoneRule> winner =
                    RulesFor(zoneRules, function) is { Count: > 0 } zoneMatch ? zoneMatch :
                    RulesFor(unitRules, function) is { Count: > 0 } unitMatch ? unitMatch :
                    RulesFor(globalRules, function);
                result.AddRange(winner);
            }
            return result;
        }

        private static List<DeviceUnitZoneRule> RulesFor(IList<DeviceUnitZoneRule> rules, RelayFunction function) =>
            rules.Where(r => r.ActionType == ActionType.Relay && r.RelayFunction == function).ToList();

        /// Same Zone>Unit>Global precedence as ResolveRelayRules, but for one zone's effective Notification-action rules, grouped by SensorMetric (null is its own group - a pure Interval/Schedule/RuleTriggered reminder rule with nothing to measure).
        public static IList<DeviceUnitZoneRule> ResolveNotificationRules(IList<DeviceUnitZoneRule> zoneRules, IList<DeviceUnitZoneRule> unitRules, IList<DeviceUnitZoneRule> globalRules)
        {
            var metrics = zoneRules.Concat(unitRules).Concat(globalRules)
                .Where(r => r.ActionType == ActionType.Notification)
                .Select(r => r.SensorMetric)
                .Distinct();

            var result = new List<DeviceUnitZoneRule>();
            foreach (SensorMetric? metric in metrics)
            {
                IList<DeviceUnitZoneRule> winner =
                    NotificationRulesFor(zoneRules, metric) is { Count: > 0 } zoneMatch ? zoneMatch :
                    NotificationRulesFor(unitRules, metric) is { Count: > 0 } unitMatch ? unitMatch :
                    NotificationRulesFor(globalRules, metric);
                result.AddRange(winner);
            }
            return result;
        }

        private static List<DeviceUnitZoneRule> NotificationRulesFor(IList<DeviceUnitZoneRule> rules, SensorMetric? metric) =>
            rules.Where(r => r.ActionType == ActionType.Notification && r.SensorMetric == metric).ToList();
    }
}
