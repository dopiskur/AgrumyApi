using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #81: the Zone-cubes-within-a-unit drill-down page - the unit's own name
    /// (breadcrumb/heading) plus its zone cubes. DeviceUnitController.Zones() only renders this when
    /// the unit has more than one zone - exactly one auto-redirects straight to Zone().</summary>
    public class UnitZonesViewModel
    {
        public DeviceUnit Unit { get; set; } = new();
        public IList<DeviceUnitZoneDashboard> Zones { get; set; } = new List<DeviceUnitZoneDashboard>();
    }
}
