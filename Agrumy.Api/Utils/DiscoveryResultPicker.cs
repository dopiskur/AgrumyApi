using api.Models;

namespace api.Utils
{
    /// <summary>One winner per DiscoveredApMac: highest Rssi, tiebreak on equal Rssi is the higher
    /// (newer) ScanningDeviceID.</summary>
    public static class DiscoveryResultPicker
    {
        public static IList<DiscoveryResult> Pick(IEnumerable<DiscoveryResult> reports) =>
            reports
                .GroupBy(r => r.DiscoveredApMac)
                .Select(g => g.OrderByDescending(r => r.Rssi ?? int.MinValue).ThenByDescending(r => r.ScanningDeviceID).First())
                .ToList();
    }
}
