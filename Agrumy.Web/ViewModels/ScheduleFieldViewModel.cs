namespace api.ViewModels
{
    /// <summary>Render model for the shared _ScheduleField partial (roadmap #39) - one relay
    /// function's {enabled, daysOfWeek, start, duration} schedule group. Field names are raw
    /// strings (same convention as EnabledToggleFieldViewModel/SelectFieldViewModel), not typed
    /// expressions, because EditController.cshtml builds one of these per relay function in a loop.</summary>
    public class ScheduleFieldViewModel
    {
        public required string EnabledFieldName { get; init; }
        public required string DaysOfWeekFieldName { get; init; }
        public required string StartFieldName { get; init; }
        public required string DurationFieldName { get; init; }
        public required string Label { get; init; }

        public bool? EnabledValue { get; init; }

        /// <summary>7-bit mask, bit 0 = Sunday .. bit 6 = Saturday - matches C's tm_wday, which is
        /// what AgrumyDevice's ControllerController::scheduleRelayFunction actually compares against
        /// (see api.Models.DeviceConfigController's comment for why).</summary>
        public int? DaysOfWeekValue { get; init; }

        /// <summary>Seconds since LOCAL midnight (ServerConfig.ScheduleTimeZone) - deliberately a
        /// plain number input, not a time picker: matches every other raw-seconds field on this
        /// form (VentilationInterval etc.), and a friendlier picker is roadmap #89's job, not #39's.</summary>
        public int? StartValue { get; init; }

        public int? DurationValue { get; init; }
    }
}
