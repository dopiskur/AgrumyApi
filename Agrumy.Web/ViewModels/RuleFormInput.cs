using api.Models;

namespace api.ViewModels
{
    /// One condition slot in the Add Rule form - ConditionType null means "this slot is unused" (only valid for Condition2, Condition1 is always required). Every field for every condition type is bound here at once (same "show every type's fields together, only the selected type's are read" pattern _RuleEditor.cshtml already used pre-#212 for single-condition rules), since a JS-free form can't hide fields based on another field's value.
    public class RuleConditionInput
    {
        public ConditionType? ConditionType { get; set; }
        public LogicalOperator? Operator { get; set; }
        public double? Threshold { get; set; }
        public double? Hysteresis { get; set; }
        public int? Interval { get; set; }
        public int? IntervalLength { get; set; }
        public int? DaysOfWeek { get; set; }
        public int? Start { get; set; }
        public int? Duration { get; set; }
        public int? SunriseOffsetMinutes { get; set; }
        public int? SunsetOffsetMinutes { get; set; }
        /// RuleTriggered only - the referenced rule's id, picked from a dropdown of this SAME scope's existing Notification rules (cross-scope referencing is API-only for now, not exposed in this form).
        public int? ReferencedRuleId { get; set; }
    }

    /// Bound from DeviceUnitController's RuleAdd/UnitRuleAdd/GlobalRuleAdd forms - Conditions[0]..[MaxConditionsPerRule-1] (indexed list binding), matching the API's HardMaxConditionsPerRule.
    public class RuleFormInput
    {
        /// Mirrors DeviceUnitApiController's private HardMaxConditionsPerRule (=8, itself mirroring AgrumyFirmware's MAX_CONDITIONS_PER_RULE) - no shared constant between the two projects today.
        public const int MaxConditionsPerRule = 8;

        public ActionType ActionType { get; set; } = api.Models.ActionType.Relay;
        public RelayFunction? RelayFunction { get; set; }
        public SensorMetric? SensorMetric { get; set; }
        public string? NotificationSubject { get; set; }
        public string? NotificationBody { get; set; }
        public List<RuleConditionInput> Conditions { get; set; } = [];
    }
}
