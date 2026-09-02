using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceRepository members (roadmap #95 split, continuing #74): device diagnostics /
    /// fleet (roadmap #7 + #8), device events (roadmap #28), and the offline alert background
    /// worker (roadmap #40).</summary>
    internal partial class EfRepository
    {
        // ---- Device diagnostics / fleet (roadmap #7 + #8) --------------------------

        public async Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll)
        {
            var row = await db.DeviceDiagnostics.FirstOrDefaultAsync(d => d.DeviceID == deviceID);
            if (row == null)
            {
                row = new DeviceDiagnosticRow { DeviceID = deviceID };
                db.DeviceDiagnostics.Add(row);
            }

            row.TenantID = tenantID;
            row.LastSeenAt = DateTime.UtcNow; // server clock - device clocks drift and may lack NTP, same rule as EventDevicePushAsync
            // Keep the last known value when a field is missing (pre-#7 firmware sends only
            // ConfigVersion) so upgrading the server alone doesn't blank existing diagnostics.
            row.UptimeSeconds = poll.Uptime ?? row.UptimeSeconds;
            row.RssiDbm = poll.Rssi ?? row.RssiDbm;
            row.FreeHeapBytes = poll.FreeHeap ?? row.FreeHeapBytes;
            row.FirmwareVersion = poll.FirmwareVersion ?? row.FirmwareVersion;
            await db.SaveChangesAsync();
        }

        public async Task<IList<DeviceFleetStatus>> DeviceFleetGetAsync(int? tenantID)
        {
            IQueryable<DeviceRow> devices = db.Devices.AsNoTracking();
            if (tenantID != null)
            {
                devices = devices.Where(d => d.TenantID == tenantID);
            }

            // Left-join diagnostics (a never-seen device still shows on the dashboard) and pull the
            // newest telemetry battery as a correlated scalar subquery - translates to a plain
            // ORDER BY ... LIMIT 1 subselect on both providers, no LATERAL needed (MariaDB lacks it).
            var rows = await devices
                .Select(d => new
                {
                    Device = d,
                    Diag = db.DeviceDiagnostics.AsNoTracking()
                        .Where(x => x.DeviceID == d.IDDevice)
                        .FirstOrDefault(),
                    Battery = db.SensorData.AsNoTracking()
                        .Where(s => s.DeviceID == d.IDDevice)
                        .OrderByDescending(s => s.DateCreated)
                        .Select(s => s.Battery)
                        .FirstOrDefault(),
                })
                .ToListAsync();

            DateTime utcNow = DateTime.UtcNow;
            return rows.Select(r => new DeviceFleetStatus
            {
                IDDevice = r.Device.IDDevice,
                TenantID = r.Device.TenantID,
                DeviceName = r.Device.DeviceName,
                Enabled = r.Device.Enabled,
                SleepSeconds = r.Device.SleepSeconds,
                LastSeenAt = r.Diag?.LastSeenAt,
                UptimeSeconds = r.Diag?.UptimeSeconds,
                RssiDbm = r.Diag?.RssiDbm,
                FreeHeapBytes = r.Diag?.FreeHeapBytes,
                FirmwareVersion = r.Diag?.FirmwareVersion,
                Battery = r.Battery,
                Online = DeviceFleetStatus.ComputeOnline(r.Diag?.LastSeenAt, r.Device.SleepSeconds, utcNow),
                DeviceUnitID = r.Device.DeviceUnitID,
                DeviceUnitZoneID = r.Device.DeviceUnitZoneID,
            }).ToList();
        }

        // ---- Device events (roadmap #28) -------------------------------------------

        public async Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message)
        {
            // ServerConfigGetAsync may auto-generate the row (and its EventDedupeMinutes default) on
            // a brand-new install - same reasoning as DeviceAddAsync's own call to it.
            int dedupeMinutes = (await ServerConfigGetAsync()).EventDedupeMinutes ?? settings.EventDedupeMinutes;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-dedupeMinutes);

            bool isDuplicate = await db.EventDevices.AsNoTracking()
                .AnyAsync(e => e.DeviceID == deviceID && e.EventID == (int)eventType && e.Date >= cutoff);
            if (isDuplicate)
            {
                return false;
            }

            db.EventDevices.Add(new EventDeviceRow
            {
                DeviceID = deviceID,
                TenantID = tenantID,
                EventID = (int)eventType,
                Date = DateTime.UtcNow, // server clock, not device-reported - a device mid-"NoInternet" may lack NTP sync
                Message = message,
            });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100)
        {
            var rows = await db.EventDevices.AsNoTracking()
                .Where(e => e.DeviceID == deviceID && e.TenantID == tenantID)
                .OrderByDescending(e => e.Date)
                .Take(limit)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // ---- Offline alert background worker (roadmap #40) --------------------------

        public async Task<IList<OfflineAlertCandidate>> OfflineAlertCandidatesGetAsync()
        {
            return await db.Devices.AsNoTracking()
                .Where(d => d.Enabled == true) // a disabled device is expected to be silent
                .Select(d => new OfflineAlertCandidate(
                    d.IDDevice,
                    d.TenantID,
                    d.DeviceName,
                    d.SleepSeconds,
                    db.DeviceDiagnostics.AsNoTracking().Where(x => x.DeviceID == d.IDDevice).Select(x => x.LastSeenAt).FirstOrDefault(),
                    db.DeviceDiagnostics.AsNoTracking().Where(x => x.DeviceID == d.IDDevice).Select(x => x.OfflineNotifiedAt).FirstOrDefault()))
                .ToListAsync();
        }

        public async Task DeviceOfflineNotifiedSetAsync(int deviceID, DateTime? notifiedAt)
        {
            // A device with no diagnostic row at all has never polled, so it cannot have just
            // transitioned to offline (OfflineAlertCandidatesGetAsync's LastSeenAt would be null,
            // which ComputeOnline already treats as offline-forever) - nothing to set.
            await db.DeviceDiagnostics
                .Where(x => x.DeviceID == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OfflineNotifiedAt, notifiedAt));
        }

        private static DeviceEvent ToDto(EventDeviceRow e) => new()
        {
            IDEventDevice = e.IDEventDevice,
            DeviceID = e.DeviceID,
            // Guards against a row written by a future/older enum definition than this build's -
            // never throws, just surfaces the raw number so it's still visible in the admin list.
            EventType = Enum.IsDefined(typeof(DeviceEventType), e.EventID)
                ? ((DeviceEventType)e.EventID).ToString()
                : $"Unknown({e.EventID})",
            Message = e.Message,
            CreatedAt = e.Date,
        };
    }
}
