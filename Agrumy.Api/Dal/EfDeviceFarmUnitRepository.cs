using System.Text.Json;
using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace api.Dal
{
    /// IDeviceFarmUnitRepository, extracted out of the EfRepository god class (roadmap #246) - Unit/Zone CRUD, device assignment, and the hierarchical dashboard aggregation. Needs IServerConfigRepository (dashboard's ProblemEvent settings) and IDeviceRepository (fleet-cache invalidation after assign/unassign, plus its ToDto mapper) - both already-extracted facets, so no circular dependency.
    internal sealed class EfDeviceFarmUnitRepository(AgrumyDbContext db, IOptions<AgrumySettings> settingsOptions, IServerConfigRepository serverConfigRepository, IDeviceRepository deviceRepository) : IDeviceFarmUnitRepository
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        /// A record, not the SensorData DTO - carries only what dashboard aggregation needs.
        private sealed record UnitZoneDeviceSnapshot(
            int? DeviceFarmUnitID, int? DeviceFarmUnitZoneID, bool Enabled, bool Online, bool HasRecentProblemEvent,
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

        // ---- Farm CRUD (roadmap #384) -----------------------------------

        public async Task<IList<DeviceFarm>> DeviceFarmsGetAsync(int? tenantID)
        {
            IQueryable<DeviceFarmRow> q = db.DeviceFarms.AsNoTracking();
            if (tenantID != null)
            {
                q = q.Where(f => f.TenantID == tenantID);
            }
            var rows = await q.OrderBy(f => f.DeviceFarmName).ToListAsync();
            return rows.Select(ToDtoFarm).ToList();
        }

        public async Task<DeviceFarm?> DeviceFarmGetByIdAsync(int? idDeviceFarm)
        {
            var row = await db.DeviceFarms.AsNoTracking().FirstOrDefaultAsync(f => f.IDDeviceFarm == idDeviceFarm);
            return row == null ? null : ToDtoFarm(row);
        }

        public async Task<DeviceFarm> DeviceFarmAddAsync(DeviceFarm farm)
        {
            var row = new DeviceFarmRow { TenantID = farm.TenantID, DeviceFarmName = farm.DeviceFarmName };
            db.DeviceFarms.Add(row);
            await db.SaveChangesAsync();
            return ToDtoFarm(row);
        }

        public async Task DeviceFarmUpdateAsync(DeviceFarm farm)
        {
            var row = await db.DeviceFarms.FirstOrDefaultAsync(f => f.IDDeviceFarm == farm.IDDeviceFarm);
            if (row == null)
            {
                return;
            }
            // TenantID intentionally not overwritten - same "payload cannot move to another tenant" rule as DeviceFarmUnitUpdateAsync.
            row.DeviceFarmName = farm.DeviceFarmName;
            await db.SaveChangesAsync();
        }

        public async Task DeviceFarmDeleteAsync(int idDeviceFarm)
        {
            // Units stay valid, just unassigned - same "delete the parent, keep the child" rule as DeviceUnassignFromZoneAsync, not a cascade delete.
            await db.DeviceFarmUnits.Where(u => u.DeviceFarmID == idDeviceFarm)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeviceFarmID, (int?)null));

            // Farm-scope rules (DeviceFarmID set, DeviceFarmUnitID/DeviceFarmUnitZoneID both null) live directly on the farm - they'd otherwise survive as orphans after the farm is gone.
            var farmRuleIds = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmID == idDeviceFarm).Select(r => r.IDDeviceFarmUnitZoneRule).ToListAsync();
            await db.RuleNotificationStates.Where(s => farmRuleIds.Contains(s.RuleID)).ExecuteDeleteAsync();
            await db.DeviceFarmUnitZoneRules.Where(r => r.DeviceFarmID == idDeviceFarm).ExecuteDeleteAsync();

            await db.DeviceFarms.Where(f => f.IDDeviceFarm == idDeviceFarm).ExecuteDeleteAsync();
        }

        // ---- Unit CRUD -------------------------------------------------

        public async Task<IList<DeviceFarmUnit>> DeviceFarmUnitsGetAsync(int? tenantID)
        {
            IQueryable<DeviceFarmUnitRow> q = db.DeviceFarmUnits.AsNoTracking().Where(u => u.IDDeviceFarmUnit != 0);
            if (tenantID != null)
            {
                q = q.Where(u => u.TenantID == tenantID);
            }
            var rows = await q.OrderBy(u => u.DeviceFarmUnitName).ToListAsync();
            return rows.Select(ToDtoUnit).ToList();
        }

        public async Task<DeviceFarmUnit?> DeviceFarmUnitGetByIdAsync(int? idDeviceFarmUnit)
        {
            var row = await db.DeviceFarmUnits.AsNoTracking().FirstOrDefaultAsync(u => u.IDDeviceFarmUnit == idDeviceFarmUnit);
            return row == null ? null : ToDtoUnit(row);
        }

        public async Task<DeviceFarmUnit> DeviceFarmUnitAddAsync(DeviceFarmUnit unit)
        {
            // IDDeviceFarmUnit is ValueGeneratedNever - MySQL's default sql_mode treats an explicit 0 on an AUTO_INCREMENT column as "generate a new value", which would collide with the reserved IDDeviceFarmUnit=0 sentinel (Math.Max(...,1) below keeps 0 free).
            for (int attempt = 0; ; attempt++)
            {
                int nextId = Math.Max((await db.DeviceFarmUnits.AsNoTracking().Select(u => (int?)u.IDDeviceFarmUnit).MaxAsync() ?? 0) + 1, 1);
                var row = new DeviceFarmUnitRow { IDDeviceFarmUnit = nextId, TenantID = unit.TenantID, DeviceFarmUnitName = unit.DeviceFarmUnitName, DeviceFarmID = unit.DeviceFarmID };
                db.DeviceFarmUnits.Add(row);
                try
                {
                    await db.SaveChangesAsync();
                    return ToDtoUnit(row);
                }
                catch (DbUpdateException) when (attempt < 4)
                {
                    // Two concurrent adds computed the same MAX+1 - detach the failed row and retry against a freshly read max, rather than surfacing the PK collision to the caller.
                    db.Entry(row).State = EntityState.Detached;
                }
            }
        }

        public async Task DeviceFarmUnitUpdateAsync(DeviceFarmUnit unit)
        {
            var row = await db.DeviceFarmUnits.FirstOrDefaultAsync(u => u.IDDeviceFarmUnit == unit.IDDeviceFarmUnit);
            if (row == null)
            {
                return;
            }
            // TenantID intentionally not overwritten - same "payload cannot move to another tenant" rule as DeviceUpdateAsync.
            row.DeviceFarmUnitName = unit.DeviceFarmUnitName;
            row.DeviceFarmID = unit.DeviceFarmID;
            await db.SaveChangesAsync();
        }

        public async Task DeviceFarmUnitDeleteAsync(int idDeviceFarmUnit)
        {
            var zoneIds = await db.DeviceFarmUnitZones.AsNoTracking()
                .Where(z => z.DeviceFarmUnitID == idDeviceFarmUnit)
                .Select(z => z.IDDeviceFarmUnitZone)
                .ToListAsync();

            foreach (int zoneId in zoneIds)
            {
                await DeviceFarmUnitZoneDeleteAsync(zoneId);
            }

            // Unit-scope rules (DeviceFarmUnitZoneID == null) live directly on the unit, not any of its zones - the zone loop above never touches them, so they'd otherwise survive as orphans after the unit is gone.
            var unitRuleIds = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmUnitID == idDeviceFarmUnit && r.DeviceFarmUnitZoneID == null).Select(r => r.IDDeviceFarmUnitZoneRule).ToListAsync();
            await db.RuleNotificationStates.Where(s => unitRuleIds.Contains(s.RuleID)).ExecuteDeleteAsync();
            await db.DeviceFarmUnitZoneRules.Where(r => r.DeviceFarmUnitID == idDeviceFarmUnit && r.DeviceFarmUnitZoneID == null).ExecuteDeleteAsync();

            await db.DeviceFarmUnits.Where(u => u.IDDeviceFarmUnit == idDeviceFarmUnit).ExecuteDeleteAsync();
        }

        // ---- Zone CRUD ------------------------------------------------

        public async Task<IList<DeviceFarmUnitZone>> DeviceFarmUnitZonesGetAsync(int idDeviceFarmUnit)
        {
            var rows = await db.DeviceFarmUnitZones.AsNoTracking()
                .Where(z => z.DeviceFarmUnitID == idDeviceFarmUnit && z.IDDeviceFarmUnitZone != 0)
                .OrderBy(z => z.DeviceFarmUnitZoneName)
                .ToListAsync();
            return rows.Select(ToDtoZone).ToList();
        }

        public async Task<DeviceFarmUnitZone?> DeviceFarmUnitZoneGetByIdAsync(int? idDeviceFarmUnitZone)
        {
            var row = await db.DeviceFarmUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceFarmUnitZone == idDeviceFarmUnitZone);
            return row == null ? null : ToDtoZone(row);
        }

        public async Task<DeviceFarmUnitZone> DeviceFarmUnitZoneAddAsync(DeviceFarmUnitZone zone)
        {
            // Same manual max+1 reasoning, and same collision-retry, as DeviceFarmUnitAddAsync.
            for (int attempt = 0; ; attempt++)
            {
                int nextId = Math.Max((await db.DeviceFarmUnitZones.AsNoTracking().Select(z => (int?)z.IDDeviceFarmUnitZone).MaxAsync() ?? 0) + 1, 1);
                var row = new DeviceFarmUnitZoneRow
                {
                    IDDeviceFarmUnitZone = nextId,
                    TenantID = zone.TenantID,
                    DeviceFarmUnitID = zone.DeviceFarmUnitID,
                    DeviceFarmUnitZoneName = zone.DeviceFarmUnitZoneName,
                    WaterPumpMaxRunSeconds = settings.WaterPumpMaxRunSeconds,
                    WaterPumpCooldownSeconds = settings.WaterPumpCooldownSeconds,
                    // No server-wide default makes sense for a specific tank's own calibration - unlike WaterPumpMaxRunSeconds above, always taken from the caller (null/unset is the correct "no tank tracking yet" state).
                    TankCapacityLiters = zone.TankCapacityLiters,
                    WaterLevelRawEmpty = zone.WaterLevelRawEmpty,
                    WaterLevelRawFull = zone.WaterLevelRawFull,
                    // Same reasoning as Tank* above - no server-wide default, always taken from the caller.
                    HeatingMaxRunSeconds = zone.HeatingMaxRunSeconds,
                    VentilationMaxRunSeconds = zone.VentilationMaxRunSeconds,
                };
                db.DeviceFarmUnitZones.Add(row);
                try
                {
                    await db.SaveChangesAsync();
                    return ToDtoZone(row);
                }
                catch (DbUpdateException) when (attempt < 4)
                {
                    db.Entry(row).State = EntityState.Detached;
                }
            }
        }

        public async Task DeviceFarmUnitZoneUpdateAsync(DeviceFarmUnitZone zone)
        {
            var row = await db.DeviceFarmUnitZones.FirstOrDefaultAsync(z => z.IDDeviceFarmUnitZone == zone.IDDeviceFarmUnitZone);
            if (row == null)
            {
                return;
            }
            // TenantID/DeviceFarmUnitID intentionally not overwritten - renaming a zone must not silently move it to another unit or tenant.
            row.DeviceFarmUnitZoneName = zone.DeviceFarmUnitZoneName;
            row.WaterPumpMaxRunSeconds = zone.WaterPumpMaxRunSeconds;
            row.WaterPumpCooldownSeconds = zone.WaterPumpCooldownSeconds;
            row.SkipWaterPumpWhenRainPredicted = zone.SkipWaterPumpWhenRainPredicted;
            row.TankCapacityLiters = zone.TankCapacityLiters;
            row.WaterLevelRawEmpty = zone.WaterLevelRawEmpty;
            row.WaterLevelRawFull = zone.WaterLevelRawFull;
            row.HeatingMaxRunSeconds = zone.HeatingMaxRunSeconds;
            row.VentilationMaxRunSeconds = zone.VentilationMaxRunSeconds;
            await db.SaveChangesAsync();
            await DeviceFarmUnitZoneConfigVersionBumpAsync(idDeviceFarmUnitZone: row.IDDeviceFarmUnitZone);
        }

        /// Bumps ConfigVersion for every device in the zone (bulk update, not fetch-then-loop) so the next poll picks up a zone-level rule/safety-limit change.
        public async Task DeviceFarmUnitZoneConfigVersionBumpAsync(int idDeviceFarmUnitZone)
        {
            await db.Devices.Where(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
        }

        public async Task DeviceFarmUnitZoneDeleteAsync(int idDeviceFarmUnitZone)
        {
            var deviceIds = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone)
                .Select(d => d.IDDevice)
                .ToListAsync();

            foreach (int deviceId in deviceIds)
            {
                await DeviceUnassignFromZoneAsync(deviceId);
            }

            // App-level cleanup, not a DB-level CASCADE - see AgrumyDbContext's DeviceFarmUnitZoneRuleRow config, DeleteBehavior.NoAction. Zone-cascade deletion does not run the RulesReferencingAsync guard RuleDeleteAsync uses - a whole-zone delete already unassigns its devices unconditionally, same "cascade wins" precedent.
            var ruleIds = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmUnitZoneID == idDeviceFarmUnitZone).Select(r => r.IDDeviceFarmUnitZoneRule).ToListAsync();
            await db.RuleNotificationStates.Where(s => ruleIds.Contains(s.RuleID) || s.DeviceFarmUnitZoneID == idDeviceFarmUnitZone).ExecuteDeleteAsync();
            await db.DeviceFarmUnitZoneRules.Where(r => r.DeviceFarmUnitZoneID == idDeviceFarmUnitZone).ExecuteDeleteAsync();

            await db.DeviceFarmUnitZones.Where(z => z.IDDeviceFarmUnitZone == idDeviceFarmUnitZone).ExecuteDeleteAsync();
        }

        // ---- Rules (Zone/Unit/Global scope) --------------------------------------

        public async Task<IList<DeviceFarmUnitZoneRule>> RulesGetForZoneAsync(int idDeviceFarmUnitZone)
        {
            var rows = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmUnitZoneID == idDeviceFarmUnitZone)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceFarmUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<IList<DeviceFarmUnitZoneRule>> RulesGetForUnitAsync(int idDeviceFarmUnit)
        {
            var rows = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmUnitID == idDeviceFarmUnit)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceFarmUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<IList<DeviceFarmUnitZoneRule>> RulesGetForFarmAsync(int idDeviceFarm)
        {
            var rows = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.DeviceFarmID == idDeviceFarm)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceFarmUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<IList<DeviceFarmUnitZoneRule>> RulesGetForTenantGlobalAsync(int tenantId)
        {
            var rows = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.DeviceFarmID == null && r.DeviceFarmUnitID == null && r.DeviceFarmUnitZoneID == null)
                .OrderBy(r => r.RelayFunction).ThenBy(r => r.SensorMetric).ThenBy(r => r.IDDeviceFarmUnitZoneRule)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        /// Every Notification-action rule for the tenant across all three scopes - RuleNotificationEvaluator resolves Zone>Unit>Global itself per zone, so this deliberately returns the flat, unresolved set.
        public async Task<IList<DeviceFarmUnitZoneRule>> RulesGetNotificationRulesForTenantAsync(int tenantId)
        {
            var rows = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.ActionType == (int)ActionType.Notification)
                .ToListAsync();
            return rows.Select(ToDtoRule).ToList();
        }

        public async Task<DeviceFarmUnitZoneRule?> RuleGetByIdAsync(int? idRule)
        {
            var row = await db.DeviceFarmUnitZoneRules.AsNoTracking().FirstOrDefaultAsync(r => r.IDDeviceFarmUnitZoneRule == idRule);
            return row == null ? null : ToDtoRule(row);
        }

        public async Task<int> RuleAddAsync(DeviceFarmUnitZoneRule rule)
        {
            var row = new DeviceFarmUnitZoneRuleRow
            {
                TenantID = rule.TenantID,
                DeviceFarmID = rule.DeviceFarmID,
                DeviceFarmUnitID = rule.DeviceFarmUnitID,
                DeviceFarmUnitZoneID = rule.DeviceFarmUnitZoneID,
                ActionType = (int)rule.ActionType,
                RelayFunction = (int?)rule.RelayFunction,
                SensorMetric = (int?)rule.SensorMetric,
                Conditions = JsonSerializer.Serialize(rule.Conditions, ConditionConfigJson.Options),
                NotificationSubject = rule.NotificationSubject,
                NotificationBody = rule.NotificationBody,
            };
            db.DeviceFarmUnitZoneRules.Add(row);
            await db.SaveChangesAsync();
            if (rule.DeviceFarmUnitZoneID is int idZone)
            {
                await DeviceFarmUnitZoneConfigVersionBumpAsync(idZone);
            }
            else if (rule.DeviceFarmUnitID is int idUnit)
            {
                await db.Devices.Where(d => d.DeviceFarmUnitID == idUnit)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            else if (rule.DeviceFarmID is int idFarm)
            {
                var unitIdsInFarm = db.DeviceFarmUnits.AsNoTracking().Where(u => u.DeviceFarmID == idFarm).Select(u => u.IDDeviceFarmUnit);
                await db.Devices.Where(d => d.DeviceFarmUnitID != null && unitIdsInFarm.Contains(d.DeviceFarmUnitID!.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            else
            {
                await db.Devices.Where(d => d.TenantID == rule.TenantID)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            return row.IDDeviceFarmUnitZoneRule;
        }

        /// Every RuleTriggered condition anywhere in the tenant's rules that references ruleId - callers use this to block deleting a still-referenced rule, and RuleNotificationEvaluator uses it to find dependents of a just-fired rule.
        public async Task<IList<DeviceFarmUnitZoneRule>> RulesReferencingAsync(int ruleId, int tenantId)
        {
            var candidates = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .Where(r => r.TenantID == tenantId && r.ActionType == (int)ActionType.Notification)
                .ToListAsync();
            var result = new List<DeviceFarmUnitZoneRule>();
            foreach (var row in candidates)
            {
                DeviceFarmUnitZoneRule dto = ToDtoRule(row);
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
            var row = await db.DeviceFarmUnitZoneRules.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IDDeviceFarmUnitZoneRule == idRule);
            if (row == null) { return; }

            await db.RuleNotificationStates.Where(s => s.RuleID == idRule).ExecuteDeleteAsync();
            await db.DeviceFarmUnitZoneRules.Where(r => r.IDDeviceFarmUnitZoneRule == idRule).ExecuteDeleteAsync();

            if (row.DeviceFarmUnitZoneID is int idZone)
            {
                await DeviceFarmUnitZoneConfigVersionBumpAsync(idZone);
            }
            else if (row.DeviceFarmUnitID is int idUnit)
            {
                await db.Devices.Where(d => d.DeviceFarmUnitID == idUnit)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
            else
            {
                await db.Devices.Where(d => d.TenantID == row.TenantID)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConfigVersion, d => (d.ConfigVersion ?? 0) + 1));
            }
        }

        /// False (not just missing) for a (rule, zone) pair with no row yet - a rule that has never fired for this zone has never been "true".
        public async Task<bool> RuleNotificationWasTrueGetAsync(int ruleId, int idDeviceFarmUnitZone) =>
            await db.RuleNotificationStates.AsNoTracking()
                .Where(s => s.RuleID == ruleId && s.DeviceFarmUnitZoneID == idDeviceFarmUnitZone)
                .Select(s => (bool?)s.WasTrue).FirstOrDefaultAsync() ?? false;

        public async Task RuleNotificationWasTrueSetAsync(int ruleId, int idDeviceFarmUnitZone, bool wasTrue, DateTime? lastFiredAtUtc)
        {
            var row = await db.RuleNotificationStates.FirstOrDefaultAsync(s => s.RuleID == ruleId && s.DeviceFarmUnitZoneID == idDeviceFarmUnitZone);
            if (row == null)
            {
                row = new RuleNotificationStateRow { RuleID = ruleId, DeviceFarmUnitZoneID = idDeviceFarmUnitZone };
                db.RuleNotificationStates.Add(row);
            }
            row.WasTrue = wasTrue;
            if (lastFiredAtUtc is DateTime firedAt)
            {
                row.LastFiredAtUtc = firedAt;
            }
            await db.SaveChangesAsync();
        }

        private static DeviceFarmUnitZoneRule ToDtoRule(DeviceFarmUnitZoneRuleRow r) => new()
        {
            IDDeviceFarmUnitZoneRule = r.IDDeviceFarmUnitZoneRule,
            TenantID = r.TenantID,
            DeviceFarmID = r.DeviceFarmID,
            DeviceFarmUnitID = r.DeviceFarmUnitID,
            DeviceFarmUnitZoneID = r.DeviceFarmUnitZoneID,
            ActionType = (ActionType)r.ActionType,
            RelayFunction = (RelayFunction?)r.RelayFunction,
            SensorMetric = (SensorMetric?)r.SensorMetric,
            Conditions = JsonSerializer.Deserialize<List<RuleCondition>>(r.Conditions, ConditionConfigJson.Options) ?? [],
            NotificationSubject = r.NotificationSubject,
            NotificationBody = r.NotificationBody,
        };

        public async Task<bool> DeviceFarmUnitZoneHasControllerAsync(int idDeviceFarmUnitZone)
        {
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone && d.DeviceControllerEnabled == true);
        }

        public async Task<Device?> DeviceFarmUnitZoneGetControllerAsync(int idDeviceFarmUnitZone)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone && d.DeviceControllerEnabled == true);
            return row == null ? null : EfDeviceRepository.ToDto(row);
        }

        public async Task<IList<Device>> DeviceFarmUnitGetControllersAsync(int idDeviceFarmUnit)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitID == idDeviceFarmUnit && d.DeviceControllerEnabled == true)
                .ToListAsync();
            return rows.Select(EfDeviceRepository.ToDto).ToList();
        }

        public async Task<IList<Device>> DeviceFarmUnitZoneGetSensorsAsync(int idDeviceFarmUnitZone)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone && d.DeviceSensorEnabled == true && d.DeviceControllerEnabled != true)
                .ToListAsync();
            return rows.Select(EfDeviceRepository.ToDto).ToList();
        }

        public async Task<IList<Device>> DeviceFarmUnitGetSensorsAsync(int idDeviceFarmUnit)
        {
            var rows = await db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitID == idDeviceFarmUnit && d.DeviceSensorEnabled == true && d.DeviceControllerEnabled != true)
                .ToListAsync();
            return rows.Select(EfDeviceRepository.ToDto).ToList();
        }

        // ---- Device assignment -----------------------------------------

        public async Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable)
        {
            IQueryable<DeviceRow> q = db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitZoneID == null);
            if (tenantID != null)
            {
                q = q.Where(d => d.TenantID == tenantID);
            }
            q = controllerCapable
                ? q.Where(d => d.DeviceControllerEnabled == true)
                : q.Where(d => d.DeviceSensorEnabled == true);

            var rows = await q.ToListAsync();
            return rows.Select(EfDeviceRepository.ToDto).ToList();
        }

        public async Task DeviceAssignToZoneAsync(int idDevice, int idDeviceFarmUnitZone)
        {
            var zone = await db.DeviceFarmUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceFarmUnitZone == idDeviceFarmUnitZone);
            var device = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (zone == null || device == null)
            {
                return;
            }

            device.DeviceFarmUnitID = zone.DeviceFarmUnitID;
            device.DeviceFarmUnitZoneID = zone.IDDeviceFarmUnitZone;
            // Bumped (unlike Unassign below) - the device learns its new assignment on its next poll.
            device.ConfigVersion = (device.ConfigVersion ?? 0) + 1;
            await db.SaveChangesAsync();
            await deviceRepository.InvalidateFleetCacheAsync(device.TenantID);
        }

        public async Task DeviceUnassignFromZoneAsync(int idDevice)
        {
            int? tenantID = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice).Select(d => (int?)d.TenantID).FirstOrDefaultAsync();
            // No ConfigVersion bump - the device is not notified, it just stops counting toward any zone's aggregation.
            await db.Devices.Where(d => d.IDDevice == idDevice)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.DeviceFarmUnitID, (int?)null)
                    .SetProperty(d => d.DeviceFarmUnitZoneID, (int?)null));
            await deviceRepository.InvalidateFleetCacheAsync(tenantID);
        }

        // ---- Dashboard aggregation -------------------------------------

        public async Task<IList<DeviceFarmUnitDashboard>> DeviceFarmUnitDashboardGetAsync(int? tenantID)
        {
            IQueryable<DeviceFarmUnitRow> units = db.DeviceFarmUnits.AsNoTracking().Where(u => u.IDDeviceFarmUnit != 0);
            if (tenantID != null)
            {
                units = units.Where(u => u.TenantID == tenantID);
            }
            var unitRows = await units.ToListAsync();

            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking()
                .Where(d => d.DeviceFarmUnitID != null);
            if (tenantID != null)
            {
                scopedDevices = scopedDevices.Where(d => d.TenantID == tenantID);
            }
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            var zonesByUnit = (await db.DeviceFarmUnitZones.AsNoTracking()
                .Where(z => z.IDDeviceFarmUnitZone != 0)
                .Select(z => new { z.DeviceFarmUnitID, z.IDDeviceFarmUnitZone })
                .ToListAsync())
                .GroupBy(z => z.DeviceFarmUnitID)
                .ToDictionary(g => g.Key, g => g.Select(z => z.IDDeviceFarmUnitZone).ToList());

            // One SensorData query for every unit's zones combined, not one query per unit in the loop below - a fleet with 20+ units otherwise pulls its whole 24h trend window once per unit.
            var zoneIdsByUnit = unitRows.ToDictionary(u => u.IDDeviceFarmUnit, u => zonesByUnit.GetValueOrDefault(u.IDDeviceFarmUnit) ?? []);
            var trendsByUnit = await BuildTrendsByZoneGroupAsync(zoneIdsByUnit);

            var result = new List<DeviceFarmUnitDashboard>();
            foreach (var u in unitRows)
            {
                var scoped = snapshots.Where(s => s.DeviceFarmUnitID == u.IDDeviceFarmUnit).ToList();
                var zoneIds = zonesByUnit.GetValueOrDefault(u.IDDeviceFarmUnit) ?? [];
                result.Add(new DeviceFarmUnitDashboard
                {
                    IDDeviceFarmUnit = u.IDDeviceFarmUnit,
                    DeviceFarmUnitName = u.DeviceFarmUnitName,
                    ZoneCount = zoneIds.Count,
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped),
                    Status = ComputeStatus(scoped),
                    Trend = trendsByUnit[u.IDDeviceFarmUnit],
                    ProblemAlerts = alerts.Where(a => a.DeviceFarmUnitID == u.IDDeviceFarmUnit).Select(ToDtoAlert).ToList(),
                });
            }
            return result;
        }

        public async Task<IList<DeviceFarmUnitZoneDashboard>> DeviceFarmUnitZoneDashboardListGetAsync(int idDeviceFarmUnit)
        {
            var zoneRows = await db.DeviceFarmUnitZones.AsNoTracking()
                .Where(z => z.DeviceFarmUnitID == idDeviceFarmUnit && z.IDDeviceFarmUnitZone != 0)
                .ToListAsync();

            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking().Where(d => d.DeviceFarmUnitID == idDeviceFarmUnit);
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            // Same batching as DeviceFarmUnitDashboardGetAsync - one query for every zone's trend instead of one per zone in the loop below.
            var trendsByZone = await BuildTrendsByZoneGroupAsync(zoneRows.ToDictionary(z => z.IDDeviceFarmUnitZone, z => (List<int>)[z.IDDeviceFarmUnitZone]));

            var result = new List<DeviceFarmUnitZoneDashboard>();
            foreach (var z in zoneRows)
            {
                var scoped = snapshots.Where(s => s.DeviceFarmUnitZoneID == z.IDDeviceFarmUnitZone).ToList();
                result.Add(new DeviceFarmUnitZoneDashboard
                {
                    IDDeviceFarmUnitZone = z.IDDeviceFarmUnitZone,
                    IDDeviceFarmUnit = z.DeviceFarmUnitID,
                    DeviceFarmUnitZoneName = z.DeviceFarmUnitZoneName,
                    DeviceCount = scoped.Count,
                    Averages = Average(scoped, z),
                    Status = ComputeStatus(scoped),
                    Trend = trendsByZone[z.IDDeviceFarmUnitZone],
                    ProblemAlerts = alerts.Where(a => a.DeviceFarmUnitZoneID == z.IDDeviceFarmUnitZone).Select(ToDtoAlert).ToList(),
                });
            }
            return result;
        }

        public async Task<DeviceFarmUnitZoneDashboard?> DeviceFarmUnitZoneDashboardGetAsync(int idDeviceFarmUnitZone)
        {
            var zone = await db.DeviceFarmUnitZones.AsNoTracking().FirstOrDefaultAsync(z => z.IDDeviceFarmUnitZone == idDeviceFarmUnitZone);
            if (zone == null)
            {
                return null;
            }

            var deviceRows = await db.Devices.AsNoTracking().Where(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone).ToListAsync();
            IQueryable<DeviceRow> scopedDevices = db.Devices.AsNoTracking().Where(d => d.DeviceFarmUnitZoneID == idDeviceFarmUnitZone);
            (int expiryHours, bool alertsEnabled) = await ProblemEventSettingsAsync();
            var snapshots = await GetDeviceSnapshotsAsync(scopedDevices, expiryHours, alertsEnabled);
            var alerts = await GetProblemAlertsAsync(scopedDevices, expiryHours, alertsEnabled);

            return new DeviceFarmUnitZoneDashboard
            {
                IDDeviceFarmUnitZone = zone.IDDeviceFarmUnitZone,
                IDDeviceFarmUnit = zone.DeviceFarmUnitID,
                DeviceFarmUnitZoneName = zone.DeviceFarmUnitZoneName,
                DeviceCount = deviceRows.Count,
                Averages = Average(snapshots, zone),
                Devices = deviceRows.Select(EfDeviceRepository.ToDto).ToList(),
                Status = ComputeStatus(snapshots),
                Trend = await BuildTrendAsync([idDeviceFarmUnitZone]),
                ProblemAlerts = alerts.Select(ToDtoAlert).ToList(),
            };
        }

        /// Single ServerConfig read shared by every dashboard aggregation call this request needs it in.
        private async Task<(int ExpiryHours, bool AlertsEnabled)> ProblemEventSettingsAsync()
        {
            ServerConfig config = await serverConfigRepository.ServerConfigGetAsync(1);
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
                    d.DeviceFarmUnitID,
                    d.DeviceFarmUnitZoneID,
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
                // Same window ComputeOnline uses to decide online/offline - a reading outside it is from a dead/unreachable sensor and must not silently count toward the zone/unit average or feed RuleNotificationEvaluator (which reads this same Averages value).
                double maxReadingAgeSeconds = (d.SleepSeconds ?? 60) * DeviceFleetStatus.OfflineMissedPolls + DeviceFleetStatus.OfflineGraceSeconds;
                if (s?.DateCreated is DateTime readingAt && (utcNow - readingAt).TotalSeconds > maxReadingAgeSeconds)
                {
                    s = null;
                }
                bool enabled = d.Enabled == true;
                // A disabled device is expected to be silent - its offline-ness must not redden a zone/unit nobody expects it to report into.
                bool online = !enabled || DeviceFleetStatus.ComputeOnline(d.LastSeenAt, d.SleepSeconds, utcNow);
                return new UnitZoneDeviceSnapshot(
                    d.DeviceFarmUnitID, d.DeviceFarmUnitZoneID, enabled, online, d.HasRecentProblemEvent,
                    s?.Temperature, s?.SoilTemperature, s?.Humidity, s?.Moisture, s?.Light,
                    s?.Co2, s?.Tvoc, s?.Barometer, s?.LiquidPH, s?.RainLevel, s?.WaterLevel, s?.Wind);
            }).ToList();
        }

        /// Carries JOIN projection fields - lets the alert list group by unit/zone without a second round trip to look either up from DeviceID.
        private sealed record UnitZoneProblemAlertRow(int? DeviceFarmUnitID, int? DeviceFarmUnitZoneID, int IDEventDevice, int DeviceID, string? DeviceName, int EventID, DateTime? Date, string? Message);

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
                    (d, e) => new { d.DeviceFarmUnitID, d.DeviceFarmUnitZoneID, e.IDEventDevice, d.IDDevice, d.DeviceName, e.EventID, e.Date, e.Message })
                .OrderByDescending(a => a.Date)
                .ToListAsync();
            return rows.Select(a => new UnitZoneProblemAlertRow(a.DeviceFarmUnitID, a.DeviceFarmUnitZoneID, a.IDEventDevice, a.IDDevice, a.DeviceName, a.EventID, a.Date, a.Message)).ToList();
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

        /// Last-24h hourly average per sensor type across the given zones - filters sensorData directly by DeviceFarmUnitZoneID (ix_sensorData_deviceFarmUnitZone_date) since a trend needs every reading, not just the latest.
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
                .Where(s => s.DeviceFarmUnitZoneID != null && zoneIds.Contains(s.DeviceFarmUnitZoneID.Value) && s.DateCreated >= cutoff)
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

        /// Same 24h hourly-bucket trend as BuildTrendAsync, but for many keys (units, or zones) in one SensorData query instead of one query per key - each zone belongs to exactly one key, so results just get routed by DeviceFarmUnitZoneID after the single fetch.
        private async Task<Dictionary<TKey, SensorTrend>> BuildTrendsByZoneGroupAsync<TKey>(Dictionary<TKey, List<int>> zoneIdsByKey) where TKey : notnull
        {
            var result = zoneIdsByKey.Keys.ToDictionary(k => k, _ => new SensorTrend());
            var keyByZoneId = new Dictionary<int, TKey>();
            foreach (var (key, zoneIds) in zoneIdsByKey)
            {
                foreach (int zoneId in zoneIds)
                {
                    keyByZoneId[zoneId] = key;
                }
            }
            if (keyByZoneId.Count == 0)
            {
                return result;
            }

            DateTime utcNow = DateTime.UtcNow;
            DateTime cutoff = utcNow.AddHours(-SensorTrend.HourBuckets);
            var zoneIdList = keyByZoneId.Keys.ToList();

            var rows = await db.SensorData.AsNoTracking()
                .Where(s => s.DeviceFarmUnitZoneID != null && zoneIdList.Contains(s.DeviceFarmUnitZoneID.Value) && s.DateCreated >= cutoff)
                .Select(s => new
                {
                    s.DeviceFarmUnitZoneID, s.DateCreated, s.Temperature, s.SoilTemperature, s.Humidity, s.Moisture, s.Light,
                    s.Co2, s.Tvoc, s.Barometer, s.LiquidPH, s.RainLevel, s.WaterLevel, s.Wind,
                })
                .ToListAsync();

            var byKeyAndBucket = rows
                .Where(r => r.DateCreated != null)
                .Select(r => (Key: keyByZoneId[r.DeviceFarmUnitZoneID!.Value], Bucket: HourBucketIndex(r.DateCreated!.Value, utcNow), Row: r))
                .Where(x => x.Bucket >= 0 && x.Bucket < SensorTrend.HourBuckets)
                .GroupBy(x => (x.Key, x.Bucket));

            foreach (var group in byKeyAndBucket)
            {
                var trend = result[group.Key.Key];
                var rowsInBucket = group.Select(x => x.Row).ToList();
                trend.Temperature[group.Key.Bucket] = rowsInBucket.Select(r => r.Temperature).Average();
                trend.SoilTemperature[group.Key.Bucket] = rowsInBucket.Select(r => r.SoilTemperature).Average();
                trend.Humidity[group.Key.Bucket] = rowsInBucket.Select(r => r.Humidity).Average();
                trend.Vpd[group.Key.Bucket] = rowsInBucket.Select(r => VpdCalculator.Compute(r.Temperature, r.Humidity)).Average();
                trend.Moisture[group.Key.Bucket] = rowsInBucket.Select(r => r.Moisture).Average();
                trend.Light[group.Key.Bucket] = rowsInBucket.Select(r => r.Light).Average();
                trend.Co2[group.Key.Bucket] = rowsInBucket.Select(r => r.Co2).Average();
                trend.Tvoc[group.Key.Bucket] = rowsInBucket.Select(r => r.Tvoc).Average();
                trend.Barometer[group.Key.Bucket] = rowsInBucket.Select(r => r.Barometer).Average();
                trend.LiquidPH[group.Key.Bucket] = rowsInBucket.Select(r => r.LiquidPH).Average();
                trend.RainLevel[group.Key.Bucket] = rowsInBucket.Select(r => r.RainLevel).Average();
                trend.WaterLevel[group.Key.Bucket] = rowsInBucket.Select(r => r.WaterLevel).Average();
                trend.Wind[group.Key.Bucket] = rowsInBucket.Select(r => r.Wind).Average();
            }
            return result;
        }

        /// 0 = the bucket ending 24h ago, 23 = the current hour - a timestamp outside the 24h window (or, defensively, in the future) falls outside [0, HourBuckets), which the caller filters out.
        private static int HourBucketIndex(DateTime dateCreated, DateTime utcNow) =>
            SensorTrend.HourBuckets - 1 - (int)Math.Floor((utcNow - dateCreated).TotalHours);

        /// Per-sensor-type average across snapshots - LINQ's nullable Average() already ignores nulls and returns null (not an exception) for an all-null source, exactly "no device reported this type". zone is only passed at Zone granularity - a Unit rollup passes null since it may span zones with different (or no) tank calibration, and TankFillPercent/VolumeLiters stay null there.
        private static SensorAverages Average(IReadOnlyCollection<UnitZoneDeviceSnapshot> snapshots, DeviceFarmUnitZoneRow? zone = null)
        {
            double? waterLevel = snapshots.Select(s => s.WaterLevel).Average();
            // Fill fraction is linear, so averaging raw WaterLevel first and calibrating once is equivalent to calibrating per device then averaging.
            (double? tankFillPercent, double? tankVolumeLiters) = zone == null
                ? (null, null)
                : TankCalculator.Compute(waterLevel, zone.WaterLevelRawEmpty, zone.WaterLevelRawFull, zone.TankCapacityLiters);

            return new SensorAverages
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
                WaterLevel = waterLevel,
                Wind = snapshots.Select(s => s.Wind).Average(),
                TankFillPercent = tankFillPercent,
                TankVolumeLiters = tankVolumeLiters,
            };
        }

        // ---- Tank refill alert (roadmap #234) --------------------------

        public async Task<IList<TankRefillAlertCandidate>> TankRefillAlertCandidatesGetAsync()
        {
            var zones = await db.DeviceFarmUnitZones.AsNoTracking()
                .Where(z => z.IDDeviceFarmUnitZone != 0 && z.TenantID != null
                    && z.TankCapacityLiters != null && z.WaterLevelRawEmpty != null && z.WaterLevelRawFull != null)
                .ToListAsync();

            var result = new List<TankRefillAlertCandidate>();
            foreach (var z in zones)
            {
                // Latest reading per device in the zone (portable scalar subquery, same shape as LowBatteryAlertCandidatesGetAsync's Battery column), averaged client-side.
                var latestPerDevice = await db.Devices.AsNoTracking()
                    .Where(d => d.DeviceFarmUnitZoneID == z.IDDeviceFarmUnitZone)
                    .Select(d => db.SensorData.AsNoTracking()
                        .Where(s => s.DeviceID == d.IDDevice)
                        .OrderByDescending(s => s.DateCreated)
                        .Select(s => (int?)s.WaterLevel)
                        .FirstOrDefault())
                    .ToListAsync();
                double? waterLevel = latestPerDevice.Select(w => (double?)w).Average();

                result.Add(new TankRefillAlertCandidate(
                    z.IDDeviceFarmUnitZone, z.TenantID!.Value, z.DeviceFarmUnitZoneName,
                    waterLevel, z.WaterLevelRawEmpty, z.WaterLevelRawFull, z.TankCapacityLiters, z.TankRefillNotifiedAt));
            }
            return result;
        }

        public async Task TankRefillNotifiedSetAsync(int idDeviceFarmUnitZone, DateTime? notifiedAt)
        {
            await db.DeviceFarmUnitZones.Where(z => z.IDDeviceFarmUnitZone == idDeviceFarmUnitZone)
                .ExecuteUpdateAsync(s => s.SetProperty(z => z.TankRefillNotifiedAt, notifiedAt));
        }

        // ---- Manual actuate (roadmap #219) --------------------------

        public async Task ManualOverrideStartAsync(DeviceManualOverride manualOverride)
        {
            var row = await db.DeviceManualOverrides
                .FirstOrDefaultAsync(o => o.DeviceID == manualOverride.DeviceID && o.RelayFunction == (int)manualOverride.RelayFunction);
            if (row == null)
            {
                row = new DeviceManualOverrideRow { DeviceID = manualOverride.DeviceID, RelayFunction = (int)manualOverride.RelayFunction };
                db.DeviceManualOverrides.Add(row);
            }
            row.TenantID = manualOverride.TenantID;
            row.Mode = (int)manualOverride.Mode;
            row.StartedAtUtc = manualOverride.StartedAtUtc;
            row.ExpiresAtUtc = manualOverride.ExpiresAtUtc;
            row.TargetMetric = (int?)manualOverride.TargetMetric;
            row.TargetThreshold = manualOverride.TargetThreshold;
            row.TargetHysteresis = manualOverride.TargetHysteresis;
            await db.SaveChangesAsync();
        }

        public async Task ManualOverrideStopAsync(int deviceId, RelayFunction relayFunction)
        {
            await db.DeviceManualOverrides
                .Where(o => o.DeviceID == deviceId && o.RelayFunction == (int)relayFunction)
                .ExecuteDeleteAsync();
        }

        public async Task<IList<DeviceManualOverride>> ManualOverridesActiveForDeviceAsync(int deviceId)
        {
            DateTime utcNow = DateTime.UtcNow;
            var rows = await db.DeviceManualOverrides.AsNoTracking()
                .Where(o => o.DeviceID == deviceId && o.ExpiresAtUtc > utcNow)
                .ToListAsync();
            return rows.Select(ToDtoManualOverride).ToList();
        }

        private static DeviceManualOverride ToDtoManualOverride(DeviceManualOverrideRow o) => new()
        {
            IDDeviceManualOverride = o.IDDeviceManualOverride,
            DeviceID = o.DeviceID,
            TenantID = o.TenantID,
            RelayFunction = (RelayFunction)o.RelayFunction,
            Mode = (ManualOverrideMode)o.Mode,
            StartedAtUtc = o.StartedAtUtc,
            ExpiresAtUtc = o.ExpiresAtUtc,
            TargetMetric = o.TargetMetric is int tm ? (SensorMetric)tm : null,
            TargetThreshold = o.TargetThreshold,
            TargetHysteresis = o.TargetHysteresis,
        };

        private static DeviceFarmUnit ToDtoUnit(DeviceFarmUnitRow u) => new()
        {
            IDDeviceFarmUnit = u.IDDeviceFarmUnit,
            TenantID = u.TenantID,
            DeviceFarmUnitName = u.DeviceFarmUnitName,
            DeviceFarmID = u.DeviceFarmID,
        };

        private static DeviceFarm ToDtoFarm(DeviceFarmRow f) => new()
        {
            IDDeviceFarm = f.IDDeviceFarm,
            TenantID = f.TenantID,
            DeviceFarmName = f.DeviceFarmName,
        };

        private static DeviceFarmUnitZone ToDtoZone(DeviceFarmUnitZoneRow z) => new()
        {
            IDDeviceFarmUnitZone = z.IDDeviceFarmUnitZone,
            TenantID = z.TenantID,
            DeviceFarmUnitID = z.DeviceFarmUnitID,
            DeviceFarmUnitZoneName = z.DeviceFarmUnitZoneName,
            WaterPumpMaxRunSeconds = z.WaterPumpMaxRunSeconds,
            WaterPumpCooldownSeconds = z.WaterPumpCooldownSeconds,
            SkipWaterPumpWhenRainPredicted = z.SkipWaterPumpWhenRainPredicted,
            TankCapacityLiters = z.TankCapacityLiters,
            WaterLevelRawEmpty = z.WaterLevelRawEmpty,
            WaterLevelRawFull = z.WaterLevelRawFull,
            HeatingMaxRunSeconds = z.HeatingMaxRunSeconds,
            VentilationMaxRunSeconds = z.VentilationMaxRunSeconds,
        };
    }
}
