using api.Models;

namespace api.ViewModels
{
    public class UnitZonesViewModel
    {
        public DeviceUnit Unit { get; set; } = new();
        public IList<DeviceUnitZoneDashboard> Zones { get; set; } = new List<DeviceUnitZoneDashboard>();
    }
}
