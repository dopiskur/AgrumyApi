using api.Models;

namespace api.ViewModels
{
    public enum RuleScope
    {
        Zone,
        Unit,
        Global,
    }

    /// Drives _RuleEditor.cshtml, shared across the Zone page, Unit "Rules" tab, and the tenant-wide Global Rules page (roadmap #212) - the three scopes differ only in which API routes/hidden field they post to.
    public class RuleEditorViewModel
    {
        public required RuleScope Scope { get; init; }

        /// IDDeviceUnitZone for Zone scope, IDDeviceUnit for Unit scope, null for Global (implied by the caller's tenant).
        public int? ScopeId { get; init; }

        public IList<DeviceUnitZoneRule> Rules { get; init; } = [];

        public string AddActionName => Scope switch
        {
            RuleScope.Zone => "RuleAdd",
            RuleScope.Unit => "UnitRuleAdd",
            _ => "GlobalRuleAdd",
        };

        public string DeleteActionName => Scope switch
        {
            RuleScope.Zone => "RuleDelete",
            RuleScope.Unit => "UnitRuleDelete",
            _ => "GlobalRuleDelete",
        };

        /// "" for Global, where there's no scope id to carry - RuleAdd's own scope check (server-side) resolves it from the caller's tenant instead.
        public string ScopeHiddenFieldName => Scope switch
        {
            RuleScope.Zone => "idDeviceUnitZone",
            RuleScope.Unit => "idDeviceUnit",
            _ => "",
        };

        /// Which page RuleAdd/RuleDelete redirect back to, so this same partial works unmodified across all three host pages.
        public required string RedirectActionName { get; init; }
    }
}
