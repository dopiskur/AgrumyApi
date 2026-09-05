using api.Dal.Entities;
using api.Dal.Interface;
using api.Firmware;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDeviceRepository members: device diagnostics/fleet, device events, and the offline/low-battery alert background workers.
    internal partial class EfRepository
    {
        // ---- Device diagnostics / fleet --------------------------

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
            // Keep the last known value when a field is missing so upgrading the server alone doesn't blank existing diagnostics.
            row.UptimeSeconds = poll.Uptime ?? row.UptimeSeconds;
            row.RssiDbm = poll.Rssi ?? row.RssiDbm;
            row.FreeHeapBytes = poll.FreeHeap ?? row.FreeHeapBytes;
            row.FirmwareVersion = poll.FirmwareVersion ?? row.FirmwareVersion;
            row.Board = poll.Board ?? row.Board;
            row.Kit = poll.Kit ?? row.Kit;
            await db.SaveChangesAsync();
        }

        // Short absolute-TTL cache so any number of concurrently open admin tabs share one real fleet query per window instead of each re-running the full per-device scan.
        private static readonly TimeSpan FleetCacheTtl = TimeSpan.FromSeconds(6);

        public async Task<IList<DeviceFleetStatus>> DeviceFleetGetAsync(int? tenantID)
        {
            string cacheKey = $"fleet:{tenantID?.ToString() ?? "global"}";
            List<DeviceFleetStatus>? cached = await cache.GetAsync<List<DeviceFleetStatus>>(cacheKey);
            if (cached != null)
            {
                return cached;
            }

            IQueryable<DeviceRow> devices = db.Devices.AsNoTracking();
            if (tenantID != null)
            {
                devices = devices.Where(d => d.TenantID == tenantID);
            }

            List<DeviceFleetStatus> result = (await BuildFleetStatusesAsync(devices))
                // Fleet page default view: unassigned devices surfaced first, newest device first within each group.
                .OrderBy(d => d.DeviceUnitID == null ? 0 : 1)
                .ThenByDescending(d => d.IDDevice)
                .ToList();

            await cache.SetAsync(cacheKey, result, FleetCacheTtl);
            return result;
        }

        /// A write that changes a device's fleet row (e.g. zone assignment) must drop both its own-tenant and the GlobalAdmin's cached snapshot, or the next Fleet read can still serve the pre-write result for up to FleetCacheTtl.
        private Task InvalidateFleetCacheAsync(int? tenantID) => Task.WhenAll(
            cache.RemoveAsync("fleet:global"),
            tenantID != null ? cache.RemoveAsync($"fleet:{tenantID}") : Task.CompletedTask);

        /// Same status one row of DeviceFleetGetAsync would carry, without loading the rest of the fleet - for a single-device detail page. Not cached (DeviceFleetGetAsync's cache exists to share one whole-fleet scan across concurrent Fleet page tabs, not relevant to a one-row lookup).
        public async Task<DeviceFleetStatus?> DeviceFleetStatusGetAsync(int deviceID, int? tenantID)
        {
            IQueryable<DeviceRow> devices = db.Devices.AsNoTracking().Where(d => d.IDDevice == deviceID);
            if (tenantID != null)
            {
                devices = devices.Where(d => d.TenantID == tenantID);
            }

            return (await BuildFleetStatusesAsync(devices)).FirstOrDefault();
        }

        // Left-join diagnostics (a never-seen device still shows on the dashboard) - Battery is a correlated scalar subquery (plain ORDER BY...LIMIT 1, no LATERAL needed since MariaDB lacks it).
        private async Task<List<DeviceFleetStatus>> BuildFleetStatusesAsync(IQueryable<DeviceRow> devices)
        {
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

            // Kit is a small fixed set of strings - cheap to pull entire and check in memory rather than a per-device join.
            Dictionary<string, bool> kitCapability = await db.DeviceTypeKits.AsNoTracking()
                .ToDictionaryAsync(k => k.Kit, k => k.ControllerCapable);

            // Units/Zones are a small admin-managed set - same in-memory-lookup reasoning as kitCapability above.
            Dictionary<int, string?> unitNames = await db.DeviceUnits.AsNoTracking()
                .ToDictionaryAsync(u => u.IDDeviceUnit, u => u.DeviceUnitName);
            Dictionary<int, string?> zoneNames = await db.DeviceUnitZones.AsNoTracking()
                .ToDictionaryAsync(z => z.IDDeviceUnitZone, z => z.DeviceUnitZoneName);

            // One catalog read, newest version per board picked in memory by semver (not DateAdded).
            FirmwareSource activeSource = (await ServerConfigGetAsync()).FirmwareSource;
            var visible = new HashSet<int> { (int)activeSource, (int)FirmwareSource.Local };
            var catalog = await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.Board != null && visible.Contains(f.Source))
                .Select(f => new { f.Board, f.Version })
                .ToListAsync();
            var latestPerBoard = catalog
                .GroupBy(f => f.Board!)
                .ToDictionary(g => g.Key, g => g.Select(f => f.Version).Where(FirmwareVersion.IsValid).OrderByDescending(v => FirmwareVersion.Parse(v!)).FirstOrDefault());

            DateTime utcNow = DateTime.UtcNow;
            return rows.Select(r =>
            {
                string? latest = r.Diag?.Board != null && latestPerBoard.TryGetValue(r.Diag.Board, out var v) ? v : null;
                return new DeviceFleetStatus
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
                    Board = r.Diag?.Board,
                    Kit = r.Diag?.Kit,
                    // Admin's explicit DeviceControllerEnabled choice always wins if set - a recognized Kit only adds capability, never takes it away.
                    ControllerCapable = r.Device.DeviceControllerEnabled == true
                        || (r.Diag?.Kit is { Length: > 0 } kit && kitCapability.GetValueOrDefault(kit)),
                    LatestFirmwareVersion = latest,
                    FirmwareUpdateAvailable = FirmwareVersion.IsNewer(latest, r.Diag?.FirmwareVersion),
                    FirmwareUpdatePending = r.Device.FirmwareUpdate == true,
                    FirmwareTargetVersion = r.Device.FirmwareTargetVersion,
                    Battery = r.Battery,
                    Online = DeviceFleetStatus.ComputeOnline(r.Diag?.LastSeenAt, r.Device.SleepSeconds, utcNow),
                    DeviceUnitID = r.Device.DeviceUnitID,
                    DeviceUnitZoneID = r.Device.DeviceUnitZoneID,
                    DeviceUnitName = r.Device.DeviceUnitID is int uid ? unitNames.GetValueOrDefault(uid) : null,
                    DeviceUnitZoneName = r.Device.DeviceUnitZoneID is int zid ? zoneNames.GetValueOrDefault(zid) : null,
                };
            }).ToList();
        }

        // ---- Device events -------------------------------------------

        public async Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message)
        {
            // ServerConfigGetAsync may auto-generate the row (and its EventDedupeMinutes default) on a brand-new install, same as DeviceAddAsync's own call.
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

        public async Task<bool> EventDeviceAcknowledgeAsync(int idEventDevice, int? tenantID)
        {
            // tenantID is the same value used to authorize the call, applied straight to the WHERE clause - a foreign tenant's event id can never be acknowledged even if guessable.
            IQueryable<EventDeviceRow> q = db.EventDevices.Where(e => e.IDEventDevice == idEventDevice);
            if (tenantID != null)
            {
                q = q.Where(e => e.TenantID == tenantID);
            }
            int updated = await q.ExecuteUpdateAsync(s => s.SetProperty(e => e.AcknowledgedAt, DateTime.UtcNow));
            return updated > 0;
        }

        // ---- Offline alert background worker --------------------------

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
            // A device with no diagnostic row has never polled, so it can't have just transitioned to offline - nothing to set.
            await db.DeviceDiagnostics
                .Where(x => x.DeviceID == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OfflineNotifiedAt, notifiedAt));
        }

        // ---- Low-battery alert background worker --------------------------

        public async Task<IList<LowBatteryAlertCandidate>> LowBatteryAlertCandidatesGetAsync()
        {
            // Same correlated-scalar-subquery shape as DeviceFleetGetAsync's Battery column above.
            return await db.Devices.AsNoTracking()
                .Where(d => d.Enabled == true) // a disabled device is expected to be silent
                .Select(d => new LowBatteryAlertCandidate(
                    d.IDDevice,
                    d.TenantID,
                    d.DeviceName,
                    db.SensorData.AsNoTracking()
                        .Where(s => s.DeviceID == d.IDDevice)
                        .OrderByDescending(s => s.DateCreated)
                        .Select(s => s.Battery)
                        .FirstOrDefault(),
                    db.DeviceDiagnostics.AsNoTracking().Where(x => x.DeviceID == d.IDDevice).Select(x => x.LowBatteryNotifiedAt).FirstOrDefault()))
                .ToListAsync();
        }

        public async Task DeviceLowBatteryNotifiedSetAsync(int deviceID, DateTime? notifiedAt)
        {
            // Same "nothing to set for a device that has never polled" rule as DeviceOfflineNotifiedSetAsync.
            await db.DeviceDiagnostics
                .Where(x => x.DeviceID == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LowBatteryNotifiedAt, notifiedAt));
        }

        private static DeviceEvent ToDto(EventDeviceRow e) => new()
        {
            IDEventDevice = e.IDEventDevice,
            DeviceID = e.DeviceID,
            // Guards against a row written by a future/older enum definition - never throws, just surfaces the raw number.
            EventType = Enum.IsDefined(typeof(DeviceEventType), e.EventID)
                ? ((DeviceEventType)e.EventID).ToString()
                : $"Unknown({e.EventID})",
            Message = e.Message,
            CreatedAt = e.Date,
        };
    }
}
