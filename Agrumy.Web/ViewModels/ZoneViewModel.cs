using api.Models;

namespace api.ViewModels
{
    public class ZoneViewModel
    {
        public required DeviceFarmUnitZoneDashboard Dashboard { get; init; }
        public IList<DeviceFleetStatus> Fleet { get; init; } = [];
        public string DisplayTimeZone { get; init; } = "UTC";

        /// {"sensorData":[...]} averaged across the zone's devices (SensorDataZoneAverageGetAsync); only set on the full Zone page, not the polled ZoneDetails fragment, so it isn't re-queried on every poll.
        public string? SensorDataJson { get; set; }

        // Null/empty when the zone has no controller-capable device assigned; the automation section is not shown at all in that case.
        public DeviceFarmUnitZone? Zone { get; init; }
        public IList<DeviceFarmUnitZoneRule> Rules { get; init; } = [];

        /// Roadmap #219 - currently-active manual commands (not yet past ExpiresAtUtc), same "no controller, no section" condition as Zone/Rules above.
        public IList<DeviceManualOverride> ManualOverrides { get; init; } = [];

        public IList<DiscoveryResult> DiscoveredDevices { get; set; } = [];
        public IList<TenantWifiConfig> WifiConfigs { get; set; } = [];
    }
}
