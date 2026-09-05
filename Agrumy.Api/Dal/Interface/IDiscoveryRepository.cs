using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Discovery facet: roadmap #268 "Scan for new devices" - deviceDiscoveryReport
    /// storage and its best-Rssi-pick query.</summary>
    public interface IDiscoveryRepository
    {
        Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi);

        /// <summary>One winner per DiscoveredApMac (api.Utils.DiscoveryResultPicker) among reports
        /// from scanning devices in scope - Zone if zoneId is given, else Unit if unitId is given,
        /// else every device in the tenant (Fleet-wide).</summary>
        Task<IList<DiscoveryResult>> DiscoveryResultsGetAsync(int? tenantId, int? unitId, int? zoneId);
    }
}
