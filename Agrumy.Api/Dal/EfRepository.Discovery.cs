using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IDiscoveryRepository members - forwarded to the standalone EfDiscoveryRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi) =>
            discoveryRepository.DiscoveryReportAddAsync(scanningDeviceId, discoveredApMac, rssi);

        public Task<IList<DiscoveryResult>> DiscoveryResultsGetAsync(int? tenantId, int? unitId, int? zoneId) =>
            discoveryRepository.DiscoveryResultsGetAsync(tenantId, unitId, zoneId);

        public Task<DiscoveryResult?> DiscoveryResultGetAsync(string discoveredApMac, int? tenantId) =>
            discoveryRepository.DiscoveryResultGetAsync(discoveredApMac, tenantId);
    }
}
