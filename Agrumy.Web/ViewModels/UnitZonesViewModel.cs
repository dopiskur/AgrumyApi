using api.Models;

namespace api.ViewModels
{
    public class UnitZonesViewModel
    {
        public DeviceFarmUnit Unit { get; set; } = new();
        public IList<DeviceFarmUnitZoneDashboard> Zones { get; set; } = new List<DeviceFarmUnitZoneDashboard>();
        public string DisplayTimeZone { get; set; } = "UTC";
        public IList<DiscoveryResult> DiscoveredDevices { get; set; } = new List<DiscoveryResult>();
        public IList<TenantWifiConfig> WifiConfigs { get; set; } = new List<TenantWifiConfig>();

        /// {"sensorData":[...]} averaged across every device in this unit (all its zones) - see SensorDataUnitAverageGetAsync.
        public string? SensorDataJson { get; set; }
    }
}
