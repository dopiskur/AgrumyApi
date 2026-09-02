using api.Dal.Entities;
using api.Dal.Interface;
using api.Firmware;
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
            row.Board = poll.Board ?? row.Board; // roadmap #94
            await db.SaveChangesAsync();
        }

        // Roadmap #118: the Web #90 live-refresh poll (10s, one per open admin tab) was re-running
        // this whole correlated-subquery-per-device fleet scan on every tick regardless of how many
        // tabs were open. A short absolute-TTL cache (see ICache.SetAsync) lets any number of
        // concurrently open tabs share one real DB query per window - does not fix #14's underlying
        // per-row cost as sensorData grows, only defers when that growth becomes visible.
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

            // Roadmap #93: one catalog read for the whole fleet, newest version per board picked in
            // memory (semver, not DateAdded) across the visible sources - see
            // FirmwareCatalogService.VisibleSources for why Local always counts.
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
            List<DeviceFleetStatus> result = rows.Select(r =>
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
                    LatestFirmwareVersion = latest,
                    FirmwareUpdateAvailable = FirmwareVersion.IsNewer(latest, r.Diag?.FirmwareVersion),
                    FirmwareUpdatePending = r.Device.FirmwareUpdate == true,
                    FirmwareTargetVersion = r.Device.FirmwareTargetVersion,
                    Battery = r.Battery,
                    Online = DeviceFleetStatus.ComputeOnline(r.Diag?.LastSeenAt, r.Device.SleepSeconds, utcNow),
                    DeviceUnitID = r.Device.DeviceUnitID,
                    DeviceUnitZoneID = r.Device.DeviceUnitZoneID,
                };
            }).ToList();

            await cache.SetAsync(cacheKey, result, FleetCacheTtl);
            return result;
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

        // ---- Low-battery alert background worker (roadmap #12, #40 pattern) --------------

        public async Task<IList<LowBatteryAlertCandidate>> LowBatteryAlertCandidatesGetAsync()
        {
            // Same correlated-scalar-subquery shape as DeviceFleetGetAsync's Battery column above -
            // translates to one plain ORDER BY ... LIMIT 1 subselect per device on both providers.
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
            // Same "nothing to set for a device that has never polled" rule as
            // DeviceOfflineNotifiedSetAsync - a device with no diagnostic row has no battery
            // telemetry to have been alerted about.
            await db.DeviceDiagnostics
                .Where(x => x.DeviceID == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LowBatteryNotifiedAt, notifiedAt));
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
