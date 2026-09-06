using api.Models;

namespace api.ViewModels
{
    /// Drives _RuleConditionFields.cshtml - one condition slot's input fields (1-based SlotNumber, mapped to the 0-based RuleFormInput.Conditions[SlotNumber-1] list index). IsNotification picks the ConditionType choices (RuleTriggered vs Astronomical - the two are mutually exclusive by action type, see DeviceFarmUnitApiController's RuleShapeErrorAsync).
    public record RuleConditionFieldsModel(int SlotNumber, bool IsNotification, IList<DeviceFarmUnitZoneRule> ReferenceableRules);
}
