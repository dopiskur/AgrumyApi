using api.Dal.Entities;
using api.Models;
using api.Utils;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDiscoveryRepository members.
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

        public async Task<IList<DiscoveryResult>> DiscoveryResultsGetAsync(int? tenantId, int? unitId, int? zoneId)
        {
            IQueryable<DeviceRow> scanners = db.Devices.AsNoTracking();
            if (zoneId is int zid)
            {
                scanners = scanners.Where(d => d.DeviceUnitZoneID == zid);
            }
            else if (unitId is int uid)
            {
                scanners = scanners.Where(d => d.DeviceUnitID == uid);
            }
            else if (tenantId != null)
            {
                scanners = scanners.Where(d => d.TenantID == tenantId);
            }

            var reports = await (
                from r in db.DeviceDiscoveryReports.AsNoTracking()
                join d in scanners on r.ScanningDeviceID equals d.IDDevice
                select new DiscoveryResult
                {
                    DiscoveredApMac = r.DiscoveredApMac,
                    Rssi = r.Rssi,
                    ScanningDeviceID = r.ScanningDeviceID,
                    ScanningDeviceName = d.DeviceName,
                    DateReported = r.DateReported,
                }).ToListAsync();

            return DiscoveryResultPicker.Pick(reports);
        }
    }
}
