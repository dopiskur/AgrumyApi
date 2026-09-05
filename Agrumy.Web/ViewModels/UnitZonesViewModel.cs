using api.Models;

namespace api.ViewModels
{
    public class UnitZonesViewModel
    {
        public DeviceUnit Unit { get; set; } = new();
        public IList<DeviceUnitZoneDashboard> Zones { get; set; } = new List<DeviceUnitZoneDashboard>();
        public string DisplayTimeZone { get; set; } = "UTC";
        public IList<DiscoveryResult> DiscoveredDevices { get; set; } = new List<DiscoveryResult>();
        public IList<TenantWifiConfig> WifiConfigs { get; set; } = new List<TenantWifiConfig>();

        /// {"sensorData":[...]} averaged across every device in this unit (all its zones) - see SensorDataUnitAverageGetAsync.
        public string? SensorDataJson { get; set; }
    }
}
