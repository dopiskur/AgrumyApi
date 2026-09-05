using api.Dal.Entities;

namespace api.Dal
{
    /// <summary>IDiscoveryRepository members.</summary>
    internal partial class EfRepository
    {
        public async Task DiscoveryReportAddAsync(int scanningDeviceId, string discoveredApMac, int? rssi)
        {
            db.DeviceDiscoveryReports.Add(new DeviceDiscoveryReportRow
            {
                ScanningDeviceID = scanningDeviceId,
                DiscoveredApMac = discoveredApMac,
                Rssi = rssi,
                DateReported = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }
}
