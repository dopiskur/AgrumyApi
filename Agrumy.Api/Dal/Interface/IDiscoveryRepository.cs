namespace api.Dal.Interface
{
    /// <summary>Discovery facet: roadmap #268 "Scan for new devices" - raw deviceDiscoveryReport
    /// storage only; scan-triggering and best-Rssi-pick aggregation land in later steps.</summary>
    public interface IDiscoveryRepository
    {
        Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi);
    }
}
