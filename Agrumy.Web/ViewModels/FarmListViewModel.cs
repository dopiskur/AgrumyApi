using api.Models;

namespace api.ViewModels
{
    /// Drives DeviceFarmUnit/Farms.cshtml (roadmap #384) - Units rides along so each farm row can offer an "assign unit" picker without a second round trip per row.
    public class FarmListViewModel
    {
        public IList<DeviceFarm> Farms { get; init; } = [];
        public IList<DeviceFarmUnit> Units { get; init; } = [];
    }
}
