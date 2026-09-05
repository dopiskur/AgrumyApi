using api.Models;

namespace api.Dal.Interface
{
    /// Discovery facet: "Scan for new devices" - deviceDiscoveryReport storage and its best-Rssi-pick query.
    public interface IDiscoveryRepository
    {
        Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi);

        /// One winner per DiscoveredApMac (api.Utils.DiscoveryResultPicker) among scanning devices in scope - Zone if zoneId given, else Unit if unitId given, else Fleet-wide.
        Task<IList<DiscoveryResult>> DiscoveryResultsGetAsync(int? tenantId, int? unitId, int? zoneId);

        /// <summary>Same best-pick as above, narrowed to one DiscoveredApMac - the Register flow's
        /// winning-scanning-device lookup. tenantId scopes the eligible scanning devices, so a
        /// caller can never resolve a winner from another tenant's scan report.</summary>
        Task<DiscoveryResult?> DiscoveryResultGetAsync(string discoveredApMac, int? tenantId);
    }
}
