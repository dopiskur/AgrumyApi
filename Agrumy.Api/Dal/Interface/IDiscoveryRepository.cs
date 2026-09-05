using api.Models;

namespace api.Dal.Interface
{
    /// Discovery facet: "Scan for new devices" - deviceDiscoveryReport storage and its best-Rssi-pick query.
    public interface IDiscoveryRepository
    {
        Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi);

        /// One winner per DiscoveredApMac (api.Utils.DiscoveryResultPicker) among scanning devices in scope - Zone if zoneId given, else Unit if unitId given, else Fleet-wide.
        Task<IList<DiscoveryResult>> DiscoveryResultsGetAsync(int? tenantId, int? unitId, int? zoneId);
    }
}
