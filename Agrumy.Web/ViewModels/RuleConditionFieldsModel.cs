using api.Models;

namespace api.ViewModels
{
    /// Drives _RuleConditionFields.cshtml - one condition slot's input fields (SlotNumber 1 or 2, matching RuleFormInput.Condition1/Condition2). IsNotification picks the ConditionType choices (RuleTriggered vs Astronomical - the two are mutually exclusive by action type, see DeviceUnitApiController's RuleShapeErrorAsync).
    public record RuleConditionFieldsModel(int SlotNumber, bool IsNotification, IList<DeviceUnitZoneRule> ReferenceableRules);
}
