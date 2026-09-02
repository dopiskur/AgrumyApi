using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceUnitRepository members (roadmap #74 pattern, new facet): Unit/Zone CRUD and
    /// device assignment (roadmap #82), and the hierarchical dashboard aggregation (roadmap #81).</summary>
    internal partial class EfRepository
    {
        /// <summary>One device's latest telemetry reading plus its current Unit/Zone, the shape the
        /// #81 dashboard averages are computed from. A record, not the SensorData DTO, because it
        /// only carries what aggregation needs - not DeviceID/TenantID/DateCreated.</summary>
        private sealed record UnitZoneDeviceSnapshot(
            int? DeviceUnitID, int? DeviceUnitZoneID,
            double? Temperature, double? SoilTemperature, double? Humidity, int? Moisture, int? Light,
            int? Co2, int? Tvoc, double? Barometer, double? LiquidPH, int? RainLevel, int? WaterLevel, double? Wind);

        // ---- Unit CRUD (roadmap #82) -------------------------------------------------

        public async Task<IList<DeviceUnit>> DeviceUnitsGetAsync(int? tenantID)
        {
            IQueryable<DeviceUnitRow> q = db.DeviceUnits.AsNoTracking().Where(u => u.IDDeviceUnit != 0);
            if (tenantID != null)
            {
                q = q.Where(u => u.TenantID == tenantID);
            }
            var rows = await q.OrderBy(u => u.DeviceUnitName).ToListAsync();
            return rows.Select(ToDtoUnit).ToList();
        }

        public async Task<DeviceUnit?> DeviceUnitGetByIdAsync(int? idDeviceUnit)
        {
            var row = await db.DeviceUnits.AsNoTracking().FirstOrDefaultAsync(u => u.IDDeviceUnit == idDeviceUnit);
            return row == null ? null : ToDtoUnit(row);
        }

        public async Task<DeviceUnit> DeviceUnitAddAsync(DeviceUnit unit)
        {
            // IDDeviceUnit is ValueGeneratedNever (not a DB AUTO_INCREMENT column) - see
            // DeviceEntities.cs. That is deliberate: MySQL's default sql_mode treats an explicit 0
            // on an AUTO_INCREMENT column as "generate a new value", which would silently break the
            // IDDeviceUnit=0 "Default" sentinel every unassigned device relies on. Manual max+1 is
            // the trade-off - fine at this project's single-admin-per-tenant, alpha-phase scale.
            int nextId = Math.Max((await db.DeviceUnits.AsNoTracking().Select(u => (int?)u.IDDeviceUnit).MaxAsync() ?? 0) + 1, 1);
            var row = new DeviceUnitRow { IDDeviceUnit = nextId, TenantID = unit.TenantID, DeviceUnitName = unit.DeviceUnitName };
            db.DeviceUnits.Add(row);
            await db.SaveChangesAsync();
            return ToDtoUnit(row);
        }

        public async Task DeviceUnitUpdateAsync(DeviceUnit unit)
        {
            var row = await db.DeviceUnits.FirstOrDefaultAsync(u => u.IDDeviceUnit == unit.IDDeviceUnit);
            if (row == null)
            {
                return;
            }
            // TenantID intentionally not overwritten - same "payload cannot move to another tenant" rule as DeviceUpdateAsync.
            row.DeviceUnitName = unit.DeviceUnitName;
            await db.SaveChangesAsync();
        }

        public async Task DeviceUnitDeleteAsync(int idDeviceUnit)
        {
            var zoneIds = await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.DeviceUnitID == idDeviceUnit)
                .Select(z => z.IDDeviceUnitZone)
                .ToListAsync();

            foreach (int zoneId in zoneIds)
            {
                await DeviceUnitZoneDeleteAsync(zoneId); // unassigns the zone's devices, then deletes the zone row
            }

            await db.DeviceUnits.Where(u => u.IDDeviceUnit == idDeviceUnit).ExecuteDeleteAsync();
        }

        // ---- Zone CRUD (roadmap #82) ------------------------------------------------

        public async Task<IList<DeviceUnitZone>> DeviceUnitZonesGetAsync(int idDeviceUnit)
        {
            var rows = await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.DeviceUnitID == idDeviceUnit && z.IDDeviceUnitZone != 0)
                .OrderBy(z => z.DeviceUnitZoneName)
                .ToListAsync();
            return rows.Select(ToDtoZone).ToList();
        }

        public async Task<DeviceUnitZone?> DeviceUnitZoneGetByIdAsync(int? idDeviceUnitZone)
        {
            var row = await db.DeviceUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceUnitZone == idDeviceUnitZone);
            return row == null ? null : ToDtoZone(row);
        }

        public async Task<DeviceUnitZone> DeviceUnitZoneAddAsync(DeviceUnitZone zone)
        {
            // Same manual max+1 reasoning as DeviceUnitAddAsync.
            int nextId = Math.Max((await db.DeviceUnitZones.AsNoTracking().Select(z => (int?)z.IDDeviceUnitZone).MaxAsync() ?? 0) + 1, 1);
            var row = new DeviceUnitZoneRow
            {
                IDDeviceUnitZone = nextId,
                TenantID = zone.TenantID,
                DeviceUnitID = zone.DeviceUnitID,
                DeviceUnitZoneName = zone.DeviceUnitZoneName,
            };
            db.DeviceUnitZones.Add(row);
            await db.SaveChangesAsync();
            return ToDtoZone(row);
        }

        public async Task DeviceUnitZoneUpdateAsync(DeviceUnitZone zone)
        {
            var row = await db.DeviceUnitZones.FirstOrDefaultAsync(z => z.IDDeviceUnitZone == zone.IDDeviceUnitZone);
            if (row == null)
            {
                return;
            }
            // TenantID/DeviceUnitID intentionally not overwritten - renaming a zone must not
            // silently move it to another unit or tenant.
            row.DeviceUnitZoneName = zone.DeviceUnitZoneName;
            await db.SaveChangesAsync();
        }

        public async Task DeviceUnitZoneDeleteAsync(int idDeviceUnitZone)
        {
            var deviceIds = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitZoneID == idDeviceUnitZone)
                .Select(d => d.IDDevice)
                .ToListAsync();

            foreach (int deviceId in deviceIds)
            {
                await DeviceUnassignFromZoneAsync(deviceId);
            }

            await db.DeviceUnitZones.Where(z => z.IDDeviceUnitZone == idDeviceUnitZone).ExecuteDeleteAsync();
        }

        public async Task<bool> DeviceUnitZoneHasControllerAsync(int idDeviceUnitZone)
        {
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.DeviceUnitZoneID == idDeviceUnitZone && d.DeviceControllerEnabled == true);
        }

        // ---- Device assignment (roadmap #82) -----------------------------------------

        public async Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable)
        {
            IQueryable<DeviceRow> q = db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitZoneID == null || d.DeviceUnitZoneID == 0);
            if (tenantID != null)
            {
                q = q.Where(d => d.TenantID == tenantID);
            }
            q = controllerCapable
                ? q.Where(d => d.DeviceControllerEnabled == true)
                : q.Where(d => d.DeviceSensorEnabled == true);

            var rows = await q.ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task DeviceAssignToZoneAsync(int idDevice, int idDeviceUnitZone)
        {
            var zone = await db.DeviceUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceUnitZone == idDeviceUnitZone);
            var device = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (zone == null || device == null)
            {
                return;
            }

            device.DeviceUnitID = zone.DeviceUnitID;
            device.DeviceUnitZoneID = zone.IDDeviceUnitZone;
            // Config-sync (unlike Remove below) - the device learns its new assignment on its next
            // poll, same bump DeviceUpdateAsync does for every other field change.
            device.ConfigVersion = (device.ConfigVersion ?? 0) + 1;
            await db.SaveChangesAsync();
        }

        public async Task DeviceUnassignFromZoneAsync(int idDevice)
        {
            // #82 rule (e): pure bookkeeping - no ConfigVersion bump, the device is not notified and
            // keeps polling/reporting telemetry exactly as before; it just stops counting toward any
            // zone's aggregation once the change lands.
            await db.Devices.Where(d => d.IDDevice == idDevice)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.DeviceUnitID, 0)
                    .SetProperty(d => d.DeviceUnitZoneID, 0));
        }

        // ---- Dashboard aggregation (roadmap #81) -------------------------------------

        public async Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGetAsync(int? tenantID)
        {
            IQueryable<DeviceUnitRow> units = db.DeviceUnits.AsNoTracking().Where(u => u.IDDeviceUnit != 0);
            if (tenantID != null)
            {
                units = units.Where(u => u.TenantID == tenantID);
            }
            var unitRows = await units.ToListAsync();

            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitID != null && d.DeviceUnitID != 0);
            if (tenantID != null)
            {
                scopedDevices = scopedDevices.Where(d => d.TenantID == tenantID);
            }
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices);

            var zoneCounts = await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.IDDeviceUnitZone != 0)
                .GroupBy(z => z.DeviceUnitID)
                .Select(g => new { UnitID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.UnitID, g => g.Count);

            return unitRows.Select(u =>
            {
                var scoped = snapshots.Where(s => s.DeviceUnitID == u.IDDeviceUnit).ToList();
                return new DeviceUnitDashboard
                {
                    IDDeviceUnit = u.IDDeviceUnit,
                    DeviceUnitName = u.DeviceUnitName,
                    ZoneCount = zoneCounts.GetValueOrDefault(u.IDDeviceUnit),
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped),
                };
            }).ToList();
        }

        public async Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGetAsync(int idDeviceUnit)
        {
            var zoneRows = await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.DeviceUnitID == idDeviceUnit && z.IDDeviceUnitZone != 0)
                .ToListAsync();

            var snapshots = await GetDeviceSnapshotsAsync(db.Devices.AsNoTracking().Where(d => d.DeviceUnitID == idDeviceUnit));

            return zoneRows.Select(z =>
            {
                var scoped = snapshots.Where(s => s.DeviceUnitZoneID == z.IDDeviceUnitZone).ToList();
                return new DeviceUnitZoneDashboard
                {
                    IDDeviceUnitZone = z.IDDeviceUnitZone,
                    IDDeviceUnit = z.DeviceUnitID,
                    DeviceUnitZoneName = z.DeviceUnitZoneName,
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped),
                };
            }).ToList();
        }

        public async Task<DeviceUnitZoneDashboard?> DeviceUnitZoneDashboardGetAsync(int idDeviceUnitZone)
        {
            var zone = await db.DeviceUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceUnitZone == idDeviceUnitZone);
            if (zone == null)
            {
                return null;
            }

            var deviceRows = await db.Devices.AsNoTracking().Where(d => d.DeviceUnitZoneID == idDeviceUnitZone).ToListAsync();
            var snapshots = await GetDeviceSnapshotsAsync(db.Devices.AsNoTracking().Where(d => d.DeviceUnitZoneID == idDeviceUnitZone));

            return new DeviceUnitZoneDashboard
            {
                IDDeviceUnitZone = zone.IDDeviceUnitZone,
                IDDeviceUnit = zone.DeviceUnitID,
                DeviceUnitZoneName = zone.DeviceUnitZoneName,
                DeviceCount = deviceRows.Count,
                Averages = Average(snapshots),
                Devices = deviceRows.Select(ToDto).ToList(),
            };
        }

        /// <summary>Latest telemetry per device in <paramref name="devices"/>, following the same
        /// two-step shape as DeviceFleetGetAsync's Battery subquery (a correlated subquery
        /// projecting ONE scalar column - portable across MySQL/MariaDB/Postgres, no LATERAL/APPLY
        /// needed) rather than trying to pull back a whole SensorData row in one correlated
        /// subquery, which EF cannot translate to a single-column scalar subquery.</summary>
        private async Task<List<UnitZoneDeviceSnapshot>> GetDeviceSnapshotsAsync(IQueryable<DeviceRow> devices)
        {
            var deviceLatestIds = await devices
                .Select(d => new
                {
                    d.DeviceUnitID,
                    d.DeviceUnitZoneID,
                    LatestSensorDataId = db.SensorData.AsNoTracking()
                        .Where(s => s.DeviceID == d.IDDevice)
                        .OrderByDescending(s => s.DateCreated)
                        .Select(s => (int?)s.IDSensorData)
                        .FirstOrDefault(),
                })
                .ToListAsync();

            var latestIds = deviceLatestIds.Where(x => x.LatestSensorDataId != null).Select(x => x.LatestSensorDataId!.Value).ToList();
            var latestById = latestIds.Count == 0
                ? new Dictionary<int, SensorDataRow>()
                : await db.SensorData.AsNoTracking()
                    .Where(s => latestIds.Contains(s.IDSensorData))
                    .ToDictionaryAsync(s => s.IDSensorData);

            return deviceLatestIds.Select(d =>
            {
                SensorDataRow? s = d.LatestSensorDataId != null && latestById.TryGetValue(d.LatestSensorDataId.Value, out var row) ? row : null;
                return new UnitZoneDeviceSnapshot(
                    d.DeviceUnitID, d.DeviceUnitZoneID,
                    s?.Temperature, s?.SoilTemperature, s?.Humidity, s?.Moisture, s?.Light,
                    s?.Co2, s?.Tvoc, s?.Barometer, s?.LiquidPH, s?.RainLevel, s?.WaterLevel, s?.Wind);
            }).ToList();
        }

        /// <summary>Per-sensor-type average across snapshots, each type independent - LINQ's
        /// nullable Average() already ignores null elements and returns null (not an exception) for
        /// an empty/all-null source, which is exactly "no device in scope has reported this type".</summary>
        private static SensorAverages Average(IReadOnlyCollection<UnitZoneDeviceSnapshot> snapshots) => new()
        {
            Temperature = snapshots.Select(s => s.Temperature).Average(),
            SoilTemperature = snapshots.Select(s => s.SoilTemperature).Average(),
            Humidity = snapshots.Select(s => s.Humidity).Average(),
            Moisture = snapshots.Select(s => s.Moisture).Average(),
            Light = snapshots.Select(s => s.Light).Average(),
            Co2 = snapshots.Select(s => s.Co2).Average(),
            Tvoc = snapshots.Select(s => s.Tvoc).Average(),
            Barometer = snapshots.Select(s => s.Barometer).Average(),
            LiquidPH = snapshots.Select(s => s.LiquidPH).Average(),
            RainLevel = snapshots.Select(s => s.RainLevel).Average(),
            WaterLevel = snapshots.Select(s => s.WaterLevel).Average(),
            Wind = snapshots.Select(s => s.Wind).Average(),
        };

        private static DeviceUnit ToDtoUnit(DeviceUnitRow u) => new()
        {
            IDDeviceUnit = u.IDDeviceUnit,
            TenantID = u.TenantID,
            DeviceUnitName = u.DeviceUnitName,
        };

        private static DeviceUnitZone ToDtoZone(DeviceUnitZoneRow z) => new()
        {
            IDDeviceUnitZone = z.IDDeviceUnitZone,
            TenantID = z.TenantID,
            DeviceUnitID = z.DeviceUnitID,
            DeviceUnitZoneName = z.DeviceUnitZoneName,
        };
    }
}
