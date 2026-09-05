using api.Models;

namespace api.Devices
{
    /// Resolves the CSS-cascade-style Zone>Unit>Global(per-tenant) rule precedence for one zone's Relay-action rules, per RelayFunction independently - a scope's rules for a function fully replace (not merge with) a less specific scope's, they never combine. Notification-action rules never reach this - see api.BackgroundWorkers.RuleNotificationEvaluator for their own, per-zone resolution.
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
    }
}
