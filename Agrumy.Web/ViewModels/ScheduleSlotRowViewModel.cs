using api.Models;

namespace api.ViewModels
{
    /// <summary>Render model for the shared _ScheduleSlotRow partial (roadmap #115) - one row
    /// within a ScheduleFieldViewModel's list. Index -1 renders the client-side clone template
    /// (see wwwroot/js/schedule-fields.js's Add Schedule handler) rather than a real bound row.</summary>
    public class ScheduleSlotRowViewModel
    {
        public required string ListFieldName { get; init; }
        public required int Index { get; init; }
        public DeviceScheduleSlot Slot { get; init; } = new();
    }
}
