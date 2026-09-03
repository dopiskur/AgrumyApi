using api.Models;

namespace api.ViewModels
{
    public class ZoneViewModel
    {
        public required DeviceUnitZoneDashboard Dashboard { get; init; }
        public IList<DeviceFleetStatus> Fleet { get; init; } = [];
        public string DisplayTimeZone { get; init; } = "UTC";

        // Null/empty when the zone has no controller-capable device assigned; the automation section is not shown at all in that case.
        public DeviceUnitZone? Zone { get; init; }
        public IList<DeviceUnitZoneRule> Rules { get; init; } = [];
    }
}
