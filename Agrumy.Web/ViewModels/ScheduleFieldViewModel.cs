using api.Models;

namespace api.ViewModels
{
    /// <summary>Render model for the shared _ScheduleField partial (roadmap #39/#115) - one relay
    /// function's list of schedule windows. ListFieldName is a raw string (same convention as
    /// EnabledToggleFieldViewModel/SelectFieldViewModel), not a typed expression, because
    /// EditController.cshtml builds one of these per relay function in a loop.</summary>
    public class ScheduleFieldViewModel
    {
        public required string ListFieldName { get; init; }
        public required string Label { get; init; }
        public IList<DeviceScheduleSlot> Slots { get; init; } = [];
    }
}
