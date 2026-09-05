using System.Text.Json;
using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDeviceUnitRepository members: Unit/Zone CRUD, device assignment, and the hierarchical dashboard aggregation.
    internal partial class EfRepository
    {
        /// A record, not the SensorData DTO - carries only what dashboard aggregation needs.
        private sealed record UnitZoneDeviceSnapshot(
            int? DeviceUnitID, int? DeviceUnitZoneID, bool Enabled, bool Online, bool HasRecentProblemEvent,
            double? Temperature, double? SoilTemperature, double? Humidity, int? Moisture, int? Light,
            int? Co2, int? Tvoc, double? Barometer, double? LiquidPH, int? RainLevel, int? WaterLevel, double? Wind)
        {
            /// Nonlinear formula - averaged per device below, not derived from already-averaged Temperature/Humidity.
            public double? Vpd => VpdCalculator.Compute(Temperature, Humidity);
        }

        /// Event types that make a zone/unit Orange (unless it's already Red).
        private static readonly int[] ProblemEventTypeIds =
        [
            (int)DeviceEventType.AuthFailed,
            (int)DeviceEventType.ConfigSyncFailed,
            (int)DeviceEventType.CrashLoopRollback,
            (int)DeviceEventType.OtaFailed,
            (int)DeviceEventType.Crash,
        ];

        // ---- Unit CRUD -------------------------------------------------

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
            // IDDeviceUnit is ValueGeneratedNever - MySQL's default sql_mode treats an explicit 0 on an AUTO_INCREMENT column as "generate a new value", which would collide with the reserved IDDeviceUnit=0 sentinel (Math.Max(...,1) below keeps 0 free).
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
                await DeviceUnitZoneDeleteAsync(zoneId);
            }

            await db.DeviceUnits.Where(u => u.IDDeviceUnit == idDeviceUnit).ExecuteDeleteAsync();
        }

        // ---- Zone CRUD ------------------------------------------------

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
                WaterPumpMaxRunSeconds = settings.WaterPumpMaxRunSeconds,
                WaterPumpCooldownSeconds = settings.WaterPumpCooldownSeconds,
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
            // TenantID/DeviceUnitID intentionally not overwritten - renaming a zone must not silently move it to another unit or tenant.
            row.DeviceUnitZoneName = zone.DeviceUnitZoneName;
            row.WaterPumpMaxRunSeconds = zone.WaterPumpMaxRunSeconds;
            row.WaterPumpCooldownSeconds = zone.WaterPumpCooldownSeconds;
            row.SkipWaterPumpWhenRainPredicted = zone.SkipWaterPumpWhenRainPredicted;
            await db.SaveChangesAsync();
            await DeviceUnitZoneConfigVersionBumpAsync(idDeviceUnitZone: row.IDDeviceUnitZone);
        }

        /// Bumps ConfigVersion for every device in the zone (bulk update, not fetch-then-loop) so the next poll picks up a zone-level rule/safety-limit change.
        public async Task DeviceUnitZoneConfigVersionBumpAsync(int idDeviceUnitZone)
        {
            await db.Devices.Where(d => d.DeviceUnitZoneID == idDeviceUnitZone)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
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

            // App-level cleanup, not a DB-level CASCADE - see AgrumyDbContext's DeviceUnitZoneRuleRow config, DeleteBehavior.NoAction. Zone-cascade deletion does not run the RulesReferencingAsync guard RuleDeleteAsync uses - a whole-zone delete already unassigns its devices unconditionally, same "cascade wins" precedent.
            var ruleIds = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceUnitZoneID == idDeviceUnitZone).Select(r => r.IDDeviceUnitZoneRule).ToListAsync();
            await db.RuleNotificationStates.Where(s => ruleIds.Contains(s.RuleID) || s.DeviceUnitZoneID == idDeviceUnitZone).ExecuteDeleteAsync();
            await db.DeviceUnitZoneRules.Where(r => r.DeviceUnitZoneID == idDeviceUnitZone).ExecuteDeleteAsync();

            await db.DeviceUnitZones.Where(z => z.IDDeviceUnitZone == idDeviceUnitZone).ExecuteDeleteAsync();
        }

        // ---- Rules (Zone/Unit/Global scope) --------------------------------------

        public async Task<IList<DeviceUnitZoneRule>> RulesGetForZoneAsync(int idDeviceUnitZone)
        {
            var rows = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceUnitZoneID == idDeviceUnitZone)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<IList<DeviceUnitZoneRule>> RulesGetForUnitAsync(int idDeviceUnit)
        {
            var rows = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceUnitID == idDeviceUnit)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<IList<DeviceUnitZoneRule>> RulesGetForTenantGlobalAsync(int tenantId)
        {
            var rows = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.DeviceUnitID == null && r.DeviceUnitZoneID == null)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        /// Every Notification-action rule for the tenant across all three scopes - RuleNotificationEvaluator resolves Zone>Unit>Global itself per zone, so this deliberately returns the flat, unresolved set.
        public async Task<IList<DeviceUnitZoneRule>> RulesGetNotificationRulesForTenantAsync(int tenantId)
        {
            var rows = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.ActionType == (int)ActionType.Notification)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<DeviceUnitZoneRule?> RuleGetByIdAsync(int? idRule)
        {
            var row = await db.DeviceUnitZoneRules.AsNoTracking().FirstOrDefaultAsync(r => r.IDDeviceUnitZoneRule == idRule);
            return row == null ? null : ToDtoRule(row);
        }

        public async Task<int> RuleAddAsync(DeviceUnitZoneRule rule)
        {
            var row = new DeviceUnitZoneRuleRow
            {
                TenantID = rule.TenantID,
                DeviceUnitID = rule.DeviceUnitID,
                DeviceUnitZoneID = rule.DeviceUnitZoneID,
                ActionType = (int)rule.ActionType,
                RelayFunction = (int?)rule.RelayFunction,
                SensorMetric = (int?)rule.SensorMetric,
                Conditions = System.Text.Json.JsonSerializer.Serialize(rule.Conditions, ConditionConfigJson.Options),
                NotificationSubject = rule.NotificationSubject,
                NotificationBody = rule.NotificationBody,
            };
            db.DeviceUnitZoneRules.Add(row);
            await db.SaveChangesAsync();
            if (rule.DeviceUnitZoneID is int idZone)
            {
                await DeviceUnitZoneConfigVersionBumpAsync(idZone);
            }
            else if (rule.DeviceUnitID is int idUnit)
            {
                await db.Devices.Where(d => d.DeviceUnitID == idUnit)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            else
            {
                await db.Devices.Where(d => d.TenantID == rule.TenantID)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            return row.IDDeviceUnitZoneRule;
        }

        /// Every RuleTriggered condition anywhere in the tenant's rules that references ruleId - callers use this to block deleting a still-referenced rule, and RuleNotificationEvaluator uses it to find dependents of a just-fired rule.
        public async Task<IList<DeviceUnitZoneRule>> RulesReferencingAsync(int ruleId, int tenantId)
        {
            var candidates = await db.DeviceUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.ActionType == (int)ActionType.Notification)
                .ToListAsync();
            var result = new List<DeviceUnitZoneRule>();
            foreach (var row in candidates)
            {
                DeviceUnitZoneRule dto = ToDtoRule(row);
                bool references = dto.Conditions.Any(c =>
                    c.ConditionType == ConditionType.RuleTriggered &&
                    c.ConditionConfig?.Deserialize<RuleTriggeredConditionConfig>(ConditionConfigJson.Options)?.ReferencedRuleId == ruleId);
                if (references)
                {
                    result.Add(dto);
                }
            }
            return result;
        }

        public async Task RuleDeleteAsync(int idRule)
        {
            var row = await db.DeviceUnitZoneRules.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IDDeviceUnitZoneRule == idRule);
            if (row == null) { return; }

            await db.RuleNotificationStates.Where(s => s.RuleID == idRule).ExecuteDeleteAsync();
            await db.DeviceUnitZoneRules.Where(r => r.IDDeviceUnitZoneRule == idRule).ExecuteDeleteAsync();

            if (row.DeviceUnitZoneID is int idZone)
            {
                await DeviceUnitZoneConfigVersionBumpAsync(idZone);
            }
            else if (row.DeviceUnitID is int idUnit)
            {
                await db.Devices.Where(d => d.DeviceUnitID == idUnit)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            else
            {
                await db.Devices.Where(d => d.TenantID == row.TenantID)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
        }

        /// False (not just missing) for a (rule, zone) pair with no row yet - a rule that has never fired for this zone has never been "true".
        public async Task<bool> RuleNotificationWasTrueGetAsync(int ruleId, int idDeviceUnitZone) =>
            await db.RuleNotificationStates.AsNoTracking()
                .Where(s => s.RuleID == ruleId && s.DeviceUnitZoneID == idDeviceUnitZone)
                .Select(s => (bool?)s.WasTrue).FirstOrDefaultAsync() ?? false;

        public async Task RuleNotificationWasTrueSetAsync(int ruleId, int idDeviceUnitZone, bool wasTrue, DateTime? lastFiredAtUtc)
        {
            var row = await db.RuleNotificationStates.FirstOrDefaultAsync(s => s.RuleID == ruleId && s.DeviceUnitZoneID == idDeviceUnitZone);
            if (row == null)
            {
                row = new RuleNotificationStateRow { RuleID = ruleId, DeviceUnitZoneID = idDeviceUnitZone };
                db.RuleNotificationStates.Add(row);
            }
            row.WasTrue = wasTrue;
            if (lastFiredAtUtc is DateTime firedAt)
            {
                row.LastFiredAtUtc = firedAt;
            }
            await db.SaveChangesAsync();
        }

        private static DeviceUnitZoneRule ToDtoRule(DeviceUnitZoneRuleRow r) => new()
        {
            IDDeviceUnitZoneRule = r.IDDeviceUnitZoneRule,
            TenantID = r.TenantID,
            DeviceUnitID = r.DeviceUnitID,
            DeviceUnitZoneID = r.DeviceUnitZoneID,
            ActionType = (ActionType)r.ActionType,
            RelayFunction = (RelayFunction?)r.RelayFunction,
            SensorMetric = (SensorMetric?)r.SensorMetric,
            Conditions = System.Text.Json.JsonSerializer.Deserialize<List<RuleCondition>>(r.Conditions, ConditionConfigJson.Options) ?? [],
            NotificationSubject = r.NotificationSubject,
            NotificationBody = r.NotificationBody,
        };

        public async Task<bool> DeviceUnitZoneHasControllerAsync(int idDeviceUnitZone)
        {
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.DeviceUnitZoneID == idDeviceUnitZone && d.DeviceControllerEnabled == true);
        }

        public async Task<Device?> DeviceUnitZoneGetControllerAsync(int idDeviceUnitZone)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceUnitZoneID == idDeviceUnitZone && d.DeviceControllerEnabled == true);
            return row == null ? null : ToDto(row);
        }

        public async Task<IList<Device>> DeviceUnitGetControllersAsync(int idDeviceUnit)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitID == idDeviceUnit && d.DeviceControllerEnabled == true)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<IList<Device>> DeviceUnitZoneGetSensorsAsync(int idDeviceUnitZone)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitZoneID == idDeviceUnitZone && d.DeviceSensorEnabled == true && d.DeviceControllerEnabled != true)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<IList<Device>> DeviceUnitGetSensorsAsync(int idDeviceUnit)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitID == idDeviceUnit && d.DeviceSensorEnabled == true && d.DeviceControllerEnabled != true)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // ---- Device assignment -----------------------------------------

        public async Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable)
        {
            IQueryable<DeviceRow> q = db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitZoneID == null);
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
            // Bumped (unlike Unassign below) - the device learns its new assignment on its next poll.
            device.ConfigVersion = (device.ConfigVersion ?? 0) + 1;
            await db.SaveChangesAsync();
            await InvalidateFleetCacheAsync(device.TenantID);
        }

        public async Task DeviceUnassignFromZoneAsync(int idDevice)
        {
            int? tenantID = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice).Select(d => (int?)d.TenantID).FirstOrDefaultAsync();
            // No ConfigVersion bump - the device is not notified, it just stops counting toward any zone's aggregation.
            await db.Devices.Where(d => d.IDDevice == idDevice)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.DeviceUnitID, (int?)null)
                    .SetProperty(d => d.DeviceUnitZoneID, (int?)null));
            await InvalidateFleetCacheAsync(tenantID);
        }

        // ---- Dashboard aggregation -------------------------------------

        public async Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGetAsync(int? tenantID)
        {
            IQueryable<DeviceUnitRow> units = db.DeviceUnits.AsNoTracking().Where(u => u.IDDeviceUnit != 0);
            if (tenantID != null)
            {
                units = units.Where(u => u.TenantID == tenantID);
            }
            var unitRows = await units.ToListAsync();

            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking()
                .Where(d => d.DeviceUnitID != null);
            if (tenantID != null)
            {
                scopedDevices = scopedDevices.Where(d => d.TenantID == tenantID);
            }
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            var zonesByUnit = (await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.IDDeviceUnitZone != 0)
                .Select(z => new { z.DeviceUnitID, z.IDDeviceUnitZone })
                .ToListAsync())
                .GroupBy(z => z.DeviceUnitID)
                .ToDictionary(g => g.Key, g => g.Select(z => z.IDDeviceUnitZone).ToList());

            var result = new List<DeviceUnitDashboard>();
            foreach (var u in unitRows)
            {
                var scoped = snapshots.Where(s => s.DeviceUnitID == u.IDDeviceUnit).ToList();
                var zoneIds = zonesByUnit.GetValueOrDefault(u.IDDeviceUnit) ?? [];
                result.Add(new DeviceUnitDashboard
                {
                    IDDeviceUnit = u.IDDeviceUnit,
                    DeviceUnitName = u.DeviceUnitName,
                    ZoneCount = zoneIds.Count,
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped),
                    Status = ComputeStatus(scoped),
                    Trend = await BuildTrendAsync(zoneIds),
                    ProblemAlerts = alerts.Where(a => a.DeviceUnitID == u.IDDeviceUnit).Select(ToDtoAlert).ToList(),
                });
            }
            return result;
        }

        public async Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGetAsync(int idDeviceUnit)
        {
            var zoneRows = await db.DeviceUnitZones.AsNoTracking()
                .Where(z => z.DeviceUnitID == idDeviceUnit && z.IDDeviceUnitZone != 0)
                .ToListAsync();

            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking().Where(d => d.DeviceUnitID == idDeviceUnit);
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            var result = new List<DeviceUnitZoneDashboard>();
            foreach (var z in zoneRows)
            {
                var scoped = snapshots.Where(s => s.DeviceUnitZoneID == z.IDDeviceUnitZone).ToList();
                result.Add(new DeviceUnitZoneDashboard
                {
                    IDDeviceUnitZone = z.IDDeviceUnitZone,
                    IDDeviceUnit = z.DeviceUnitID,
                    DeviceUnitZoneName = z.DeviceUnitZoneName,
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped),
                    Status = ComputeStatus(scoped),
                    Trend = await BuildTrendAsync([z.IDDeviceUnitZone]),
                    ProblemAlerts = alerts.Where(a => a.DeviceUnitZoneID == z.IDDeviceUnitZone).Select(ToDtoAlert).ToList(),
                });
            }
            return result;
        }

        public async Task<DeviceUnitZoneDashboard?> DeviceUnitZoneDashboardGetAsync(int idDeviceUnitZone)
        {
            var zone = await db.DeviceUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceUnitZone == idDeviceUnitZone);
            if (zone == null)
            {
                return null;
            }

            var deviceRows = await db.Devices.AsNoTracking().Where(d => d.DeviceUnitZoneID == idDeviceUnitZone).ToListAsync();
            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking().Where(d => d.DeviceUnitZoneID == idDeviceUnitZone);
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            return new DeviceUnitZoneDashboard
            {
                IDDeviceUnitZone = zone.IDDeviceUnitZone,
                IDDeviceUnit = zone.DeviceUnitID,
                DeviceUnitZoneName = zone.DeviceUnitZoneName,
                DeviceCount = deviceRows.Count,
                Averages = Average(snapshots),
                Devices = deviceRows.Select(ToDto).ToList(),
                Status = ComputeStatus(snapshots),
                Trend = await BuildTrendAsync([idDeviceUnitZone]),
                ProblemAlerts = alerts.Select(ToDtoAlert).ToList(),
            };
        }

        /// Single ServerConfig read shared by every dashboard aggregation call this request needs it in.
        private async Task<(int ExpiryHours, bool AlertsEnabled)> ProblemEventSettingsAsync()
        {
            ServerConfig config = await ServerConfigGetAsync();
            int expiryHours = config.ProblemEventExpiryHours > 0 ? config.ProblemEventExpiryHours : 24;
            return (expiryHours, config.ProblemEventAlertsEnabled);
        }

        /// Latest telemetry per device - EF can't translate a whole-row correlated subquery, so this pulls the latest SensorData id per device via portable scalar subqueries, then batch-fetches the rows.
        private async Task<List<UnitZoneDeviceSnapshot>> GetDeviceSnapshotsAsync(IQueryable<DeviceRow> devices, int problemEventExpiryHours, bool problemEventAlertsEnabled)
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime problemEventCutoff = utcNow.AddHours(-problemEventExpiryHours);

            var deviceLatestIds = await devices
                .Select(d => new
                {
                    d.DeviceUnitID,
                    d.DeviceUnitZoneID,
                    d.Enabled,
                    d.SleepSeconds,
                    LastSeenAt = db.DeviceDiagnostics.AsNoTracking()
                        .Where(x => x.DeviceID == d.IDDevice)
                        .Select(x => x.LastSeenAt)
                        .FirstOrDefault(),
                    HasRecentProblemEvent = problemEventAlertsEnabled && db.EventDevices.AsNoTracking()
                        .Any(e => e.DeviceID == d.IDDevice && e.AcknowledgedAt == null && e.Date >= problemEventCutoff && ProblemEventTypeIds.Contains(e.EventID)),
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
                bool enabled = d.Enabled == true;
                // A disabled device is expected to be silent - its offline-ness must not redden a zone/unit nobody expects it to report into.
                bool online = !enabled || DeviceFleetStatus.ComputeOnline(d.LastSeenAt, d.SleepSeconds, utcNow);
                return new UnitZoneDeviceSnapshot(
                    d.DeviceUnitID, d.DeviceUnitZoneID, enabled, online, d.HasRecentProblemEvent,
                    s?.Temperature, s?.SoilTemperature, s?.Humidity, s?.Moisture, s?.Light,
                    s?.Co2, s?.Tvoc, s?.Barometer, s?.LiquidPH, s?.RainLevel, s?.WaterLevel, s?.Wind);
            }).ToList();
        }

        /// Carries JOIN projection fields - lets the alert list group by unit/zone without a second round trip to look either up from DeviceID.
        private sealed record UnitZoneProblemAlertRow(int? DeviceUnitID, int? DeviceUnitZoneID, int IDEventDevice, int DeviceID, string? DeviceName, int EventID, DateTime? Date, string? Message);

        /// Every un-acknowledged problem event still inside the expiry window - same predicate as GetDeviceSnapshotsAsync's HasRecentProblemEvent, but returns the actual rows so the dashboard can show what triggered Orange.
        private async Task<List<UnitZoneProblemAlertRow>> GetProblemAlertsAsync(IQueryable<DeviceRow> devices, int problemEventExpiryHours, bool problemEventAlertsEnabled)
        {
            if (!problemEventAlertsEnabled)
            {
                return [];
            }

            DateTime cutoff = DateTime.UtcNow.AddHours(-problemEventExpiryHours);
            // OrderByDescending must run before the final Select - EF cannot translate ordering by a member of a record it just constructed inside the join's own result selector.
            var rows = await devices
                .Join(
                    db.EventDevices.AsNoTracking().Where(e => e.AcknowledgedAt == null && e.Date >= cutoff && ProblemEventTypeIds.Contains(e.EventID)),
                    d => d.IDDevice, e => e.DeviceID,
                    (d, e) => new { d.DeviceUnitID, d.DeviceUnitZoneID, e.IDEventDevice, d.IDDevice, d.DeviceName, e.EventID, e.Date, e.Message })
                .OrderByDescending(a => a.Date)
                .ToListAsync();
            return rows.Select(a => new UnitZoneProblemAlertRow(a.DeviceUnitID, a.DeviceUnitZoneID, a.IDEventDevice, a.IDDevice, a.DeviceName, a.EventID, a.Date, a.Message)).ToList();
        }

        private static UnitZoneProblemAlert ToDtoAlert(UnitZoneProblemAlertRow a) => new()
        {
            IDEventDevice = a.IDEventDevice,
            DeviceID = a.DeviceID,
            DeviceName = a.DeviceName,
            EventType = Enum.IsDefined(typeof(DeviceEventType), a.EventID) ? ((DeviceEventType)a.EventID).ToString() : $"Unknown({a.EventID})",
            Date = a.Date,
            Message = a.Message,
        };

        /// Red beats Orange beats Green - only enabled devices' online state counts toward Red, so a disabled-but-offline device turns the zone/unit Orange instead of Green.
        private static ZoneStatus ComputeStatus(IReadOnlyCollection<UnitZoneDeviceSnapshot> snapshots)
        {
            if (snapshots.Any(s => s.Enabled && !s.Online))
            {
                return ZoneStatus.Red;
            }
            if (snapshots.Any(s => s.HasRecentProblemEvent) || snapshots.Any(s => !s.Enabled))
            {
                return ZoneStatus.Orange;
            }
            return ZoneStatus.Green;
        }

        /// Last-24h hourly average per sensor type across the given zones - filters sensorData directly by DeviceUnitZoneID (ix_sensorData_deviceUnitZone_date) since a trend needs every reading, not just the latest.
        private async Task<SensorTrend> BuildTrendAsync(List<int> zoneIds)
        {
            var trend = new SensorTrend();
            if (zoneIds.Count == 0)
            {
                return trend;
            }

            DateTime utcNow = DateTime.UtcNow;
            DateTime cutoff = utcNow.AddHours(-SensorTrend.HourBuckets);

            var rows = await db.SensorData.AsNoTracking()
                .Where(s => s.DeviceUnitZoneID != null && zoneIds.Contains(s.DeviceUnitZoneID.Value) && s.DateCreated >= cutoff)
                .Select(s => new
                {
                    s.DateCreated, s.Temperature, s.SoilTemperature, s.Humidity, s.Moisture, s.Light,
                    s.Co2, s.Tvoc, s.Barometer, s.LiquidPH, s.RainLevel, s.WaterLevel, s.Wind,
                })
                .ToListAsync();

            var byBucket = rows
                .Where(r => r.DateCreated != null)
                .Select(r => (Bucket: HourBucketIndex(r.DateCreated!.Value, utcNow), Row: r))
                .Where(x => x.Bucket >= 0 && x.Bucket < SensorTrend.HourBuckets)
                .GroupBy(x => x.Bucket, x => x.Row);

            foreach (var bucket in byBucket)
            {
                var rowsInBucket = bucket.ToList();
                trend.Temperature[bucket.Key] = rowsInBucket.Select(r => r.Temperature).Average();
                trend.SoilTemperature[bucket.Key] = rowsInBucket.Select(r => r.SoilTemperature).Average();
                trend.Humidity[bucket.Key] = rowsInBucket.Select(r => r.Humidity).Average();
                trend.Vpd[bucket.Key] = rowsInBucket.Select(r => VpdCalculator.Compute(r.Temperature, r.Humidity)).Average();
                trend.Moisture[bucket.Key] = rowsInBucket.Select(r => r.Moisture).Average();
                trend.Light[bucket.Key] = rowsInBucket.Select(r => r.Light).Average();
                trend.Co2[bucket.Key] = rowsInBucket.Select(r => r.Co2).Average();
                trend.Tvoc[bucket.Key] = rowsInBucket.Select(r => r.Tvoc).Average();
                trend.Barometer[bucket.Key] = rowsInBucket.Select(r => r.Barometer).Average();
                trend.LiquidPH[bucket.Key] = rowsInBucket.Select(r => r.LiquidPH).Average();
                trend.RainLevel[bucket.Key] = rowsInBucket.Select(r => r.RainLevel).Average();
                trend.WaterLevel[bucket.Key] = rowsInBucket.Select(r => r.WaterLevel).Average();
                trend.Wind[bucket.Key] = rowsInBucket.Select(r => r.Wind).Average();
            }
            return trend;
        }

        /// 0 = the bucket ending 24h ago, 23 = the current hour - a timestamp outside the 24h window (or, defensively, in the future) falls outside [0, HourBuckets), which the caller filters out.
        private static int HourBucketIndex(DateTime dateCreated, DateTime utcNow) =>
            SensorTrend.HourBuckets - 1 - (int)Math.Floor((utcNow - dateCreated).TotalHours);

        /// Per-sensor-type average across snapshots - LINQ's nullable Average() already ignores nulls and returns null (not an exception) for an all-null source, exactly "no device reported this type".
        private static SensorAverages Average(IReadOnlyCollection<UnitZoneDeviceSnapshot> snapshots) => new()
        {
            Temperature = snapshots.Select(s => s.Temperature).Average(),
            SoilTemperature = snapshots.Select(s => s.SoilTemperature).Average(),
            Humidity = snapshots.Select(s => s.Humidity).Average(),
            Vpd = snapshots.Select(s => s.Vpd).Average(),
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
            WaterPumpMaxRunSeconds = z.WaterPumpMaxRunSeconds,
            WaterPumpCooldownSeconds = z.WaterPumpCooldownSeconds,
            SkipWaterPumpWhenRainPredicted = z.SkipWaterPumpWhenRainPredicted,
        };
    }
}
