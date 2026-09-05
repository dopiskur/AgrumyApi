using api.Models;

namespace api.ViewModels
{
    public class UnitZonesViewModel
    {
        public DeviceUnit Unit { get; set; } = new();
        public IList<DeviceUnitZoneDashboard> Zones { get; set; } = new List<DeviceUnitZoneDashboard>();
        public string DisplayTimeZone { get; set; } = "UTC";

        /// {"sensorData":[...]} averaged across every device in this unit (all its zones) - see SensorDataUnitAverageGetAsync.
        public string? SensorDataJson { get; set; }
    }
}
