using api.Models;

namespace api.ViewModels
{
    public class FleetViewModel
    {
        public IList<DeviceFleetStatus> Devices { get; set; } = new List<DeviceFleetStatus>();
        public IList<DeviceUnit> Units { get; set; } = new List<DeviceUnit>();
        public IList<DeviceUnitZone> Zones { get; set; } = new List<DeviceUnitZone>();
        public IList<DiscoveryResult> DiscoveredDevices { get; set; } = new List<DiscoveryResult>();
        public IList<TenantWifiConfig> WifiConfigs { get; set; } = new List<TenantWifiConfig>();
    }
}
