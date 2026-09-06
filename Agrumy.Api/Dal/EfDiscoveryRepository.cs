using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDiscoveryRepository, extracted out of the EfRepository god class (roadmap #246) - reads db.Devices directly for scanner scoping rather than calling into IDeviceRepository, but that's a direct DbSet read, not a facet-interface dependency.
    internal sealed class EfDiscoveryRepository(AgrumyDbContext db) : IDiscoveryRepository
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
                scanners = scanners.Where(d => d.DeviceFarmUnitZoneID == zid);
            }
            else if (unitId is int uid)
            {
                scanners = scanners.Where(d => d.DeviceFarmUnitID == uid);
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
                    TenantID = d.TenantID,
                    DateReported = r.DateReported,
                }).ToListAsync();

            return DiscoveryResultPicker.Pick(reports);
        }

        public async Task<DiscoveryResult?> DiscoveryResultGetAsync(string discoveredApMac, int? tenantId)
        {
            IQueryable<DeviceRow> scanners = db.Devices.AsNoTracking();
            if (tenantId != null)
            {
                scanners = scanners.Where(d => d.TenantID == tenantId);
            }

            var reports = await (
                from r in db.DeviceDiscoveryReports.AsNoTracking()
                where r.DiscoveredApMac == discoveredApMac
                join d in scanners on r.ScanningDeviceID equals d.IDDevice
                select new DiscoveryResult
                {
                    DiscoveredApMac = r.DiscoveredApMac,
                    Rssi = r.Rssi,
                    ScanningDeviceID = r.ScanningDeviceID,
                    ScanningDeviceName = d.DeviceName,
                    TenantID = d.TenantID,
                    DateReported = r.DateReported,
                }).ToListAsync();

            return DiscoveryResultPicker.Pick(reports).SingleOrDefault();
        }
    }
}
