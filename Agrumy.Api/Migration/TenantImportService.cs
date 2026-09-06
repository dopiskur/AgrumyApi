using api.Dal.Interface;
using api.Models;

namespace api.Migration
{
    /// Applies a TenantExport to this server (see api.Models.TenantImportTarget for ByName vs AsSentinel) - every id on the target is freshly assigned, stitched back together via the *IdMap dictionaries below.
    public class TenantImportService(IRepository repo)
    {
        /// ByName: ties to an existing tenant with this exact name, or creates one; GlobalAdmin-only at the controller layer.
        public async Task<TenantImportResult> ImportByNameAsync(TenantExport export, string targetTenantName)
        {
            int tenantId = await repo.TenantGetIdAsync(targetTenantName) ?? await repo.TenantAddAsync(targetTenantName);
            return await ImportIntoTenantAsync(export, tenantId, targetTenantName);
        }

        /// AsSentinel: TenantID=0, only while TenantZeroIsEmptyAsync - reachable without an admin session (a brand-new self-hosted server has nobody to log in as yet); discards the pending bootstrap admin placeholder first.
        public async Task<(TenantImportResult? result, string? error)> ImportAsSentinelAsync(TenantExport export)
        {
            if (!await repo.TenantZeroIsEmptyAsync())
            {
                return (null, "TenantID 0 on this server already has data - import-as-sentinel refuses to merge into or overwrite an existing installation.");
            }
            await repo.BootstrapAdminDiscardPendingAsync();
            return (await ImportIntoTenantAsync(export, 0, "Default"), null);
        }

        private async Task<TenantImportResult> ImportIntoTenantAsync(TenantExport export, int tenantId, string? tenantName)
        {
            var result = new TenantImportResult { TargetTenantId = tenantId, TargetTenantName = tenantName };

            Dictionary<int, int> userIdMap = await ImportUsersAsync(export, tenantId, result);
            Dictionary<int, int> unitIdMap = await ImportUnitsAsync(export, tenantId, result);
            Dictionary<int, int> zoneIdMap = await ImportZonesAsync(export, tenantId, unitIdMap, result);
            await ImportZoneRulesAsync(export, tenantId, zoneIdMap, result);
            Dictionary<int, int> deviceIdMap = await ImportDevicesAsync(export, tenantId, unitIdMap, zoneIdMap, result);
            await ImportSensorDataAsync(export, tenantId, deviceIdMap, unitIdMap, zoneIdMap, result);

            _ = userIdMap; // no downstream use yet (users own no FKs into other exported tables today) - kept for symmetry/future use
            return result;
        }

        private async Task<Dictionary<int, int>> ImportUsersAsync(TenantExport export, int tenantId, TenantImportResult result)
        {
            var map = new Dictionary<int, int>();
            foreach (TenantExportUser eu in export.Users)
            {
                // Email/Username carry a GLOBAL unique index (not per-tenant) - a collision (re-run import, or already has an account) is expected, so skip the row rather than fail the batch.
                if (!string.IsNullOrEmpty(eu.User.Email) && await repo.UserGetAsync(null, eu.User.Email, null) is not null)
                {
                    result.UsersSkipped++;
                    result.SkippedReasons.Add($"User {eu.User.Email}: an account with this email already exists on this server.");
                    continue;
                }
                if (!string.IsNullOrEmpty(eu.User.Username) && await repo.UserGetAsync(null, null, eu.User.Username) is not null)
                {
                    result.UsersSkipped++;
                    result.SkippedReasons.Add($"User {eu.User.Email}: username '{eu.User.Username}' already exists on this server.");
                    continue;
                }

                var newUser = new User
                {
                    TenantID = tenantId,
                    Email = eu.User.Email,
                    Username = eu.User.Username,
                    FirstName = eu.User.FirstName,
                    LastName = eu.User.LastName,
                    Phone = eu.User.Phone,
                    Enabled = eu.User.Enabled,
                    EmailVerified = eu.User.EmailVerified,
                    TimeZone = eu.User.TimeZone,
                    // Portable hash, unproven identity on THIS server - see api.Models.User.MustChangePassword.
                    MustChangePassword = true,
                };
                await repo.UserAddAsync(newUser, new UserSecret { PwdHash = eu.PwdHash, PwdSalt = eu.PwdSalt });

                // UserAddAsync doesn't return the new id - same re-fetch-by-email pattern as UserApiController.UserRegistration.
                User? added = await repo.UserGetAsync(null, eu.User.Email, null);
                if (added?.IDUser is not int newId)
                {
                    result.UsersSkipped++;
                    result.SkippedReasons.Add($"User {eu.User.Email}: did not persist.");
                    continue;
                }
                if (eu.User.IDUser is int oldId)
                {
                    map[oldId] = newId;
                }
                await repo.UserRolesSetAsync(newId, eu.Roles);
                result.UsersImported++;
            }
            return map;
        }

        private async Task<Dictionary<int, int>> ImportUnitsAsync(TenantExport export, int tenantId, TenantImportResult result)
        {
            var map = new Dictionary<int, int>();
            foreach (DeviceFarmUnit u in export.Units)
            {
                DeviceFarmUnit created = await repo.DeviceFarmUnitAddAsync(new DeviceFarmUnit { TenantID = tenantId, DeviceFarmUnitName = u.DeviceFarmUnitName });
                if (u.IDDeviceFarmUnit is int oldId && created.IDDeviceFarmUnit is int newId)
                {
                    map[oldId] = newId;
                }
                result.UnitsImported++;
            }
            return map;
        }

        // IDDeviceFarmUnit/IDDeviceFarmUnitZone=0 are shared "unassigned"/"Disabled" sentinels, same meaning on every server - never remapped, just passed through.
        private static int RemapOrSentinel(int oldId, Dictionary<int, int> map) =>
            oldId == 0 ? 0 : map.GetValueOrDefault(oldId, 0);

        private async Task<Dictionary<int, int>> ImportZonesAsync(TenantExport export, int tenantId, Dictionary<int, int> unitIdMap, TenantImportResult result)
        {
            var map = new Dictionary<int, int>();
            foreach (DeviceFarmUnitZone z in export.Zones)
            {
                DeviceFarmUnitZone created = await repo.DeviceFarmUnitZoneAddAsync(new DeviceFarmUnitZone
                {
                    TenantID = tenantId,
                    DeviceFarmUnitID = RemapOrSentinel(z.DeviceFarmUnitID, unitIdMap),
                    DeviceFarmUnitZoneName = z.DeviceFarmUnitZoneName,
                    WaterPumpMaxRunSeconds = z.WaterPumpMaxRunSeconds,
                    WaterPumpCooldownSeconds = z.WaterPumpCooldownSeconds,
                    SkipWaterPumpWhenRainPredicted = z.SkipWaterPumpWhenRainPredicted,
                    TankCapacityLiters = z.TankCapacityLiters,
                    WaterLevelRawEmpty = z.WaterLevelRawEmpty,
                    WaterLevelRawFull = z.WaterLevelRawFull,
                    HeatingMaxRunSeconds = z.HeatingMaxRunSeconds,
                    VentilationMaxRunSeconds = z.VentilationMaxRunSeconds,
                });
                if (z.IDDeviceFarmUnitZone is int oldId && created.IDDeviceFarmUnitZone is int newId)
                {
                    map[oldId] = newId;
                }
                result.ZonesImported++;
            }
            return map;
        }

        private async Task ImportZoneRulesAsync(TenantExport export, int tenantId, Dictionary<int, int> zoneIdMap, TenantImportResult result)
        {
            // Export only ever captured Zone-scoped rules (TenantExportService predates Unit/Global scope) - r.DeviceFarmUnitZoneID is
            // therefore always set here, never null.
            foreach (DeviceFarmUnitZoneRule r in export.ZoneRules.Where(r => r.DeviceFarmUnitZoneID != null))
            {
                int newZoneId = RemapOrSentinel(r.DeviceFarmUnitZoneID!.Value, zoneIdMap);
                if (newZoneId == 0)
                {
                    continue; // the zone it belonged to failed to import - an orphaned rule is worse than a dropped one
                }
                await repo.RuleAddAsync(new DeviceFarmUnitZoneRule
                {
                    TenantID = tenantId,
                    DeviceFarmUnitZoneID = newZoneId,
                    ActionType = r.ActionType,
                    RelayFunction = r.RelayFunction,
                    SensorMetric = r.SensorMetric,
                    Conditions = r.Conditions,
                    NotificationSubject = r.NotificationSubject,
                    NotificationBody = r.NotificationBody,
                });
                result.ZoneRulesImported++;
            }
        }

        private async Task<Dictionary<int, int>> ImportDevicesAsync(TenantExport export, int tenantId, Dictionary<int, int> unitIdMap, Dictionary<int, int> zoneIdMap, TenantImportResult result)
        {
            var map = new Dictionary<int, int>();
            foreach (TenantExportDevice ed in export.Devices)
            {
                // ApiId is globally unique - a collision (re-run import, or already here under a different tenant) is skipped, not failed on.
                if (!string.IsNullOrEmpty(ed.ApiId) && await repo.DeviceGetByApiIdAsync(ed.ApiId) is not null)
                {
                    result.DevicesSkipped++;
                    result.SkippedReasons.Add($"Device {ed.Device.DeviceName} ({ed.ApiId}): already exists on this server.");
                    continue;
                }

                // ApiId/ApiKey kept AS EXPORTED - a real device's firmware needs no reconfiguration to keep talking to whichever server now owns this row.
                Device created = await repo.DeviceAddAsync(new Device
                {
                    TenantID = tenantId,
                    DeviceRoleID = ed.Device.DeviceRoleID,
                    DeviceFarmUnitID = ed.Device.DeviceFarmUnitID is int u ? RemapOrSentinel(u, unitIdMap) : null,
                    DeviceFarmUnitZoneID = ed.Device.DeviceFarmUnitZoneID is int z ? RemapOrSentinel(z, zoneIdMap) : null,
                    DeviceName = ed.Device.DeviceName,
                    MacAddress = ed.Device.MacAddress,
                    ApiId = ed.ApiId,
                    ApiKey = ed.ApiKey,
                    ServicePoint = ed.Device.ServicePoint,
                    DeviceTypeServiceID = ed.Device.DeviceTypeServiceID,
                    DeviceSensorEnabled = ed.Device.DeviceSensorEnabled,
                    DeviceControllerEnabled = ed.Device.DeviceControllerEnabled,
                    BatteryEnabled = ed.Device.BatteryEnabled,
                    Enabled = ed.Device.Enabled,
                    ConfigVersion = 1,
                    IsGateway = ed.Device.IsGateway,
                    GatewayProfile = ed.Device.GatewayProfile,
                });

                if (ed.Sensor != null)
                {
                    await repo.DeviceConfigSensorUpdateAsync(created.IDDevice, ed.Sensor);
                }
                if (ed.Controller != null)
                {
                    await repo.DeviceConfigControllerUpdateAsync(created.IDDevice, ed.Controller);
                }
                if (ed.Device.IDDevice is int oldId && created.IDDevice is int newId)
                {
                    map[oldId] = newId;
                }
                result.DevicesImported++;
            }
            return map;
        }

        private async Task ImportSensorDataAsync(TenantExport export, int tenantId, Dictionary<int, int> deviceIdMap, Dictionary<int, int> unitIdMap, Dictionary<int, int> zoneIdMap, TenantImportResult result)
        {
            if (!export.IncludesSensorData || export.SensorData is not { Count: > 0 })
            {
                return;
            }

            var rows = new List<SensorData>();
            foreach (SensorData sd in export.SensorData)
            {
                // A reading for a device that itself got skipped has nowhere valid to attach - drop it rather than orphan it against device 0.
                if (sd.DeviceID is not int oldDeviceId || !deviceIdMap.TryGetValue(oldDeviceId, out int newDeviceId))
                {
                    continue;
                }
                rows.Add(new SensorData
                {
                    TenantID = tenantId,
                    DeviceID = newDeviceId,
                    DeviceFarmUnitID = sd.DeviceFarmUnitID is int u ? RemapOrSentinel(u, unitIdMap) : 0,
                    DeviceFarmUnitZoneID = sd.DeviceFarmUnitZoneID is int z ? RemapOrSentinel(z, zoneIdMap) : 0,
                    Battery = sd.Battery,
                    Temperature = sd.Temperature,
                    SoilTemperature = sd.SoilTemperature,
                    Humidity = sd.Humidity,
                    Moisture = sd.Moisture,
                    Light = sd.Light,
                    Co2 = sd.Co2,
                    Tvoc = sd.Tvoc,
                    Barometer = sd.Barometer,
                    LiquidPH = sd.LiquidPH,
                    RainLevel = sd.RainLevel,
                    WaterLevel = sd.WaterLevel,
                    Wind = sd.Wind,
                    DateCreated = sd.DateCreated,
                });
            }
            if (rows.Count > 0)
            {
                await repo.SensorDataImportAsync(rows);
                result.SensorDataRowsImported = rows.Count;
            }
        }
    }
}
