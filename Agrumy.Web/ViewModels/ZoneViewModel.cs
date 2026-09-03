using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #116 rule (5): the Zone detail page's aggregation (Dashboard) plus the
    /// same rich per-device rows Fleet.cshtml shows (Fleet, filtered to this zone) - reuses
    /// Device/_FleetRows.cshtml instead of a second hand-built device table.</summary>
    public class ZoneViewModel
    {
        public required DeviceUnitZoneDashboard Dashboard { get; init; }
        public IList<DeviceFleetStatus> Fleet { get; init; } = [];
        public string DisplayTimeZone { get; init; } = "UTC";

        // Roadmap #21: the raw zone entity (WaterPumpMaxRunSeconds/CooldownSeconds) and its rules -
        // null/empty when the zone has no controller-capable device assigned, since the automation
        // section is not shown at all in that case (see Zone.cshtml).
        public DeviceUnitZone? Zone { get; init; }
        public IList<DeviceUnitZoneRule> Rules { get; init; } = [];
    }
}
