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

    /// Bound from DeviceUnitController's RuleAdd/UnitRuleAdd/GlobalRuleAdd forms - up to 2 conditions per rule in the UI (the API/DB allow up to 8; a wider builder is a straightforward follow-up, not a hard limit here).
    public class RuleFormInput
    {
        public ActionType ActionType { get; set; } = api.Models.ActionType.Relay;
        public RelayFunction? RelayFunction { get; set; }
        public SensorMetric? SensorMetric { get; set; }
        public string? NotificationSubject { get; set; }
        public string? NotificationBody { get; set; }
        public RuleConditionInput Condition1 { get; set; } = new();
        public RuleConditionInput Condition2 { get; set; } = new();
    }
}
