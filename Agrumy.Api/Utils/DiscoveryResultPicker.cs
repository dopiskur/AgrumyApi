using api.Models;

namespace api.Utils
{
    /// One winner per DiscoveredApMac: highest Rssi, tiebreak on equal Rssi is the higher (newer) ScanningDeviceID.
    public static class DiscoveryResultPicker
    {
        public static IList<DiscoveryResult> Pick(IEnumerable<DiscoveryResult> reports) =>
            reports
                .GroupBy(r => r.DiscoveredApMac)
                .Select(g => g.OrderByDescending(r => r.Rssi ?? int.MinValue).ThenByDescending(r => r.ScanningDeviceID).First())
                .ToList();
    }
}
