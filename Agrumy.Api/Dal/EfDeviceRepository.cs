using api.Dal.Entities;
using api.Dal.Interface;
using api.Firmware;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace api.Dal
{
    /// IDeviceRepository, extracted out of the EfRepository god class (roadmap #246) - device CRUD, configs, fixed type lists, firmware's legacy board-less lookup, diagnostics/fleet, events, and the offline/low-battery alert queries. Needs IServerConfigRepository (hysteresis defaults on add, EventDedupeMinutes, active firmware source) - an already-extracted leaf facet, so no circular dependency.
    internal sealed class EfDeviceRepository(AgrumyDbContext db, IOptions<AgrumySettings> settingsOptions, ICache cache, IServerConfigRepository serverConfigRepository) : IDeviceRepository
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        public async Task<Device> DeviceAddAsync(Device device)
        {
            // Read (and possibly auto-generate) BEFORE the transaction below starts - ServerConfigGetAsync's own seed SaveChangesAsync must auto-commit before this method's explicit transaction opens, so the two never nest.
            ServerConfig serverConfig = await serverConfigRepository.ServerConfigGetAsync(1);

            await using var tx = await db.Database.BeginTransactionAsync();

            var sensorCfg = new DeviceConfigSensorRow();
            var controllerCfg = new DeviceConfigControllerRow
            {
                // Hysteresis starts at the server-wide default, overridable per device under Device -> Controller - WaterPump limits below follow the same rule.
                WaterLevelHysteresis = serverConfig.WaterLevelHysteresis,
                TemperatureHysteresis = serverConfig.TemperatureHysteresis,
                HumidityHysteresis = serverConfig.HumidityHysteresis,
                LightHysteresis = serverConfig.LightHysteresis,
                WaterPumpMaxRunSeconds = serverConfig.WaterPumpMaxRunSeconds,
                WaterPumpCooldownSeconds = serverConfig.WaterPumpCooldownSeconds,
            };
            db.DeviceConfigSensors.Add(sensorCfg);
            db.DeviceConfigControllers.Add(controllerCfg);
            await db.SaveChangesAsync();

            var row = new DeviceRow
            {
                TenantID = device.TenantID,
                DeviceRoleID = device.DeviceRoleID,
                DeviceUnitID = device.DeviceUnitID,
                DeviceUnitZoneID = device.DeviceUnitZoneID,
                DeviceName = device.DeviceName,
                MacAddress = device.MacAddress,
                ManualDeviceTypeID = device.ManualDeviceTypeID,
                ApiId = device.ApiId ?? "",
                ApiKey = device.ApiKey ?? "",
                ServicePoint = device.ServicePoint,
                DeviceTypeServiceID = device.DeviceTypeServiceID,
                DeviceSensorEnabled = device.DeviceSensorEnabled,
                DeviceConfigSensorID = sensorCfg.IDDeviceConfigSensor,
                DeviceControllerEnabled = device.DeviceControllerEnabled,
                DeviceConfigControllerID = controllerCfg.IDDeviceConfigController,
                BatteryEnabled = device.BatteryEnabled,
                Enabled = device.Enabled,
                ConfigVersion = device.ConfigVersion,
                IsGateway = device.IsGateway,
                GatewayProfile = (int?)device.GatewayProfile,
            };
            db.Devices.Add(row);
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // row.IDDevice is populated by SaveChangesAsync above - no need for a caller round-trip Get.
            return ToDto(row);
        }

        public async Task DeviceDeleteAsync(int? idDevice, int? tenantID)
        {
            var target = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice && d.TenantID == tenantID)
                .Select(d => new { d.DeviceConfigSensorID, d.DeviceConfigControllerID })
                .FirstOrDefaultAsync();
            if (target == null)
            {
                return;
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            // Diagnostics/ControllerData/Simulation first: all three FKs to device are NoAction, so leaving any would block the delete.
            await db.DeviceDiagnostics.Where(x => x.DeviceID == idDevice).ExecuteDeleteAsync();
            await db.ControllerData.Where(x => x.DeviceID == idDevice).ExecuteDeleteAsync();
            await db.DeviceSimulations.Where(x => x.DeviceID == idDevice).ExecuteDeleteAsync();
            await db.Devices.Where(d => d.IDDevice == idDevice && d.TenantID == tenantID).ExecuteDeleteAsync();
            if (target.DeviceConfigSensorID != null)
            {
                await db.DeviceConfigSensors.Where(c => c.IDDeviceConfigSensor == target.DeviceConfigSensorID).ExecuteDeleteAsync();
            }
            if (target.DeviceConfigControllerID != null)
            {
                await db.DeviceConfigControllers.Where(c => c.IDDeviceConfigController == target.DeviceConfigControllerID).ExecuteDeleteAsync();
            }
            await tx.CommitAsync();
        }

        public async Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress)
        {
            IQueryable<DeviceRow> q = db.Devices.AsNoTracking().Where(d => d.TenantID == tenantID);

            if (idDevice != null)
            {
                q = q.Where(d => d.IDDevice == idDevice);
            }
            else if (idDevice == null && apiId != null && macAddress == null)
            {
                q = q.Where(d => d.ApiId == apiId);
            }
            else if (idDevice == null && apiId == null && macAddress != null)
            {
                q = q.Where(d => d.MacAddress == macAddress);
            }
            else
            {
                return null; // no lookup key
            }

            var row = await q.FirstOrDefaultAsync();
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByIdAsync(int? idDevice)
        {
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByApiIdAsync(string? apiId)
        {
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiId == apiId);
            return row == null ? null : ToDto(row);
        }

        public async Task<IList<Device>> DevicesGetAsync(int? tenantID)
        {
            var rows = await db.Devices.AsNoTracking().Where(d => d.TenantID == tenantID).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // Same query minus the tenant filter - callers (DeviceApiController) only reach this after CallerReadsDevicesGlobally passed, mirroring UsersGetAllAsync.
        public async Task<IList<Device>> DevicesGetAllAsync()
        {
            var rows = await db.Devices.AsNoTracking().ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<IList<Device>> DevicesSensorOnlyGetAsync(int? tenantID)
        {
            IQueryable<DeviceRow> devices = db.Devices.AsNoTracking()
                .Where(d => d.DeviceSensorEnabled == true && d.DeviceControllerEnabled != true);
            if (tenantID != null)
            {
                devices = devices.Where(d => d.TenantID == tenantID);
            }
            var rows = await devices.ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress)
        {
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.TenantID == tenantID && d.MacAddress == macAddress);
        }

        public async Task DeviceUpdateAsync(Device? device)
        {
            if (device == null)
            {
                return;
            }

            var row = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == device.IDDevice);
            if (row == null)
            {
                return;
            }

            // Does not set MacAddress, config-id columns, DeviceUnitID/DeviceUnitZoneID (written exclusively by DeviceAssignToZoneAsync/DeviceUnassignFromZoneAsync to stay consistent), or ApiId/ApiKey (omitting them would wipe a device's real credential).
            row.TenantID = device.TenantID;
            row.DeviceRoleID = device.DeviceRoleID;
            row.DeviceTypeServiceID = device.DeviceTypeServiceID;
            row.DeviceName = device.DeviceName;
            row.ManualDeviceTypeID = device.ManualDeviceTypeID;
            row.ServicePoint = device.ServicePoint;
            row.ServicePublicKey = device.ServicePublicKey;
            row.SleepSeconds = device.SleepSeconds;
            row.SleepDeepEnabled = device.SleepDeepEnabled;
            row.DeviceSensorEnabled = device.DeviceSensorEnabled;
            row.DeviceControllerEnabled = device.DeviceControllerEnabled;
            row.BatteryEnabled = device.BatteryEnabled;
            row.Enabled = device.Enabled;
            row.Debug = device.Debug;
            // row's own value, not the payload's - the payload can be stale under two concurrent edits, which would otherwise let ConfigVersion regress or collide instead of growing monotonically.
            row.ConfigVersion = (row.ConfigVersion ?? 0) + 1;
            await db.SaveChangesAsync();
        }

        public Task DeviceMarkConfigSentAsync(int deviceID, DateTime sentAtUtc) =>
            db.Devices.Where(d => d.IDDevice == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastFullConfigSentAt, sentAtUtc));

        public Task DeviceHardResetSetAsync(int deviceID, bool pending) =>
            db.Devices.Where(d => d.IDDevice == deviceID)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Reset, pending));

        /// internal, not private - EfGatewayRepository and EfRepository.DeviceUnits.cs (not yet extracted) also map DeviceRow to Device.
        internal static Device ToDto(DeviceRow d) => new()
        {
            IDDevice = d.IDDevice,
            TenantID = d.TenantID,
            DeviceRoleID = d.DeviceRoleID,
            DeviceUnitID = d.DeviceUnitID,
            DeviceUnitZoneID = d.DeviceUnitZoneID,
            DeviceConfigSensorID = d.DeviceConfigSensorID,
            DeviceConfigControllerID = d.DeviceConfigControllerID,
            DeviceTypeServiceID = d.DeviceTypeServiceID,
            DeviceName = d.DeviceName,
            MacAddress = d.MacAddress,
            ManualDeviceTypeID = d.ManualDeviceTypeID,
            ApiId = d.ApiId,
            ApiKey = d.ApiKey,
            ServicePoint = d.ServicePoint,
            ServicePublicKey = d.ServicePublicKey,
            SleepSeconds = d.SleepSeconds,
            SleepDeepEnabled = d.SleepDeepEnabled,
            DeviceSensorEnabled = d.DeviceSensorEnabled,
            DeviceControllerEnabled = d.DeviceControllerEnabled,
            BatteryEnabled = d.BatteryEnabled,
            Debug = d.Debug,
            Reboot = d.Reboot,
            Reset = d.Reset,
            FirmwareUpdate = d.FirmwareUpdate,
            FirmwareTargetVersion = d.FirmwareTargetVersion,
            Enabled = d.Enabled,
            ConfigVersion = d.ConfigVersion,
            CommandVersion = d.CommandVersion,
            DateCreated = d.DateCreated,
            DateModified = d.DateModified,
            IsGateway = d.IsGateway,
            GatewayProfile = d.GatewayProfile is int p ? (GatewayProfile)p : null,
            LastFullConfigSentAt = d.LastFullConfigSentAt,
        };

        // ---- Fixed device-type lookup lists -----------------------------

        public async Task<IList<DeviceRole>> DeviceRoleGetAsync()
        {
            return await db.DeviceRoles.AsNoTracking()
                .Select(t => new DeviceRole
                {
                    IDDeviceRole = t.IDDeviceRole,
                    DeviceRoleName = t.DeviceRoleName,
                    SensorEnabled = t.SensorEnabled,
                    ControllerEnabled = t.ControllerEnabled,
                })
                .ToListAsync();
        }

        public async Task<IList<DeviceType>> DeviceTypeGetAsync()
        {
            return await db.DeviceTypes.AsNoTracking()
                .Select(k => new DeviceType { IDDeviceType = k.IDDeviceType, Kit = k.Kit, ControllerCapable = k.ControllerCapable, PinoutJson = k.PinoutJson })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync()
        {
            return await db.DeviceTypeServices.AsNoTracking()
                .Select(s => new DeviceTypeService { IDDeviceTypeService = s.IDDeviceTypeService, ServiceType = s.ServiceType })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync()
        {
            return await db.DeviceTypeRelays.AsNoTracking()
                .Select(r => new DeviceTypeRelay { IDDeviceTypeRelay = r.IDDeviceTypeRelay, RelayName = r.RelayName })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync()
        {
            return await db.DeviceTypeSensors.AsNoTracking()
                .Select(s => new DeviceTypeSensor
                {
                    IDDeviceTypeSensor = s.IDDeviceTypeSensor,
                    SensorName = s.SensorName,
                    SensorDescription = s.SensorDescription,
                    Battery = s.Battery,
                    Temperature = s.Temperature,
                    TemperatureSoil = s.TemperatureSoil,
                    Humidity = s.Humidity,
                    Moisture = s.Moisture,
                    Light = s.Light,
                    Co2 = s.Co2,
                    Tvoc = s.Tvoc,
                    Barometer = s.Barometer,
                    WaterPH = s.WaterPH,
                    WaterTankLevel = s.WaterTankLevel,
                    RainLevel = s.RainLevel,
                    Wind = s.Wind,
                })
                .ToListAsync();
        }

        // ---- Per-device sensor/controller config -----------------------

        public async Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID)
        {
            var row = await db.DeviceConfigSensors.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID)
        {
            var row = await db.DeviceConfigControllers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == deviceConfigControllerID);
            if (row == null)
            {
                return null;
            }
            IList<DeviceRelaySlot> relays = await db.DeviceConfigControllerRelays.AsNoTracking()
                .Where(r => r.IDDeviceConfigController == deviceConfigControllerID)
                .Select(r => new DeviceRelaySlot { Slot = r.Slot, RelayFunction = r.RelayFunction })
                .ToListAsync();
            return ToDto(row, relays);
        }

        public async Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigSensorID == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigControllerID == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        /// Returns null on success, or a validation error message (caller returns 400) without writing anything.
        public async Task<string?> DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return null;
            }

            var assignedSlots = cfg.Relays.Where(s => s.RelayFunction != 0).ToList();
            if (assignedSlots.Any(s => s.Slot < 1 || s.Slot > RelaySlotLimits.MaxSlots))
            {
                return $"Relay slot must be between 1 and {RelaySlotLimits.MaxSlots}.";
            }
            if (assignedSlots.Select(s => s.Slot).Distinct().Count() != assignedSlots.Count)
            {
                return "Duplicate relay slot in request.";
            }

            // Resolve from idDevice's OWN DeviceConfigControllerID, not cfg.IDDeviceConfigController - a client-supplied id could otherwise overwrite another device's controller config.
            int? ownConfigControllerId = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice)
                .Select(d => d.DeviceConfigControllerID)
                .FirstOrDefaultAsync();

            // Delete-then-insert in one transaction - a crash between the two would otherwise leave the device with zero relay slots.
            await using var transaction = await db.Database.BeginTransactionAsync();

            var row = await db.DeviceConfigControllers
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == ownConfigControllerId);
            if (row != null)
            {
                // Only the relay-pin mapping lives here now - threshold/hysteresis/interval/schedule config moved to the device's assigned DeviceUnitZone, edited from the Zone page instead.
                row.RelayEnabled = cfg.RelayEnabled;

                // Wholesale replace: delete every existing slot row for this controller, then insert one row per ASSIGNED slot (RelayFunction 0/Disabled is omitted, not stored) - simpler and less error-prone than diffing against the posted set.
                await db.DeviceConfigControllerRelays
                    .Where(r => r.IDDeviceConfigController == ownConfigControllerId)
                    .ExecuteDeleteAsync();
                foreach (DeviceRelaySlot slot in assignedSlots)
                {
                    db.DeviceConfigControllerRelays.Add(new DeviceConfigControllerRelayRow
                    {
                        IDDeviceConfigController = ownConfigControllerId!.Value,
                        Slot = slot.Slot,
                        RelayFunction = slot.RelayFunction,
                    });
                }
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return null;
        }

        public async Task DeviceConfigSensorUpdateAsync(int? idDevice, DeviceConfigSensor? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            // Same ownership-lookup rule as DeviceConfigControllerUpdateAsync above.
            int? ownConfigSensorId = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice)
                .Select(d => d.DeviceConfigSensorID)
                .FirstOrDefaultAsync();

            var row = await db.DeviceConfigSensors
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == ownConfigSensorId);
            if (row != null)
            {
                row.SensorBattery = cfg.SensorBattery;
                row.BatteryDividerR1 = cfg.BatteryDividerR1;
                row.BatteryDividerR2 = cfg.BatteryDividerR2;
                row.SensorTemp = cfg.SensorTemp;
                row.SensorTempSoil = cfg.SensorTempSoil;
                row.SensorHumid = cfg.SensorHumid;
                row.SensorMoist = cfg.SensorMoist;
                row.SensorLight = cfg.SensorLight;
                row.SensorCo2 = cfg.SensorCo2;
                row.SensorTvoc = cfg.SensorTvoc;
                row.SensorBarometer = cfg.SensorBarometer;
                row.SensorPH = cfg.SensorPH;
                row.SensorRainLevel = cfg.SensorRainLevel;
                row.SensorWaterLevel = cfg.SensorWaterLevel;
                row.SensorWind = cfg.SensorWind;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync();
        }

        private static DeviceConfigSensor ToDto(DeviceConfigSensorRow c) => new()
        {
            IDDeviceConfigSensor = c.IDDeviceConfigSensor,
            SensorBattery = c.SensorBattery,
            BatteryDividerR1 = c.BatteryDividerR1,
            BatteryDividerR2 = c.BatteryDividerR2,
            SensorTemp = c.SensorTemp,
            SensorTempSoil = c.SensorTempSoil,
            SensorHumid = c.SensorHumid,
            SensorMoist = c.SensorMoist,
            SensorLight = c.SensorLight,
            SensorCo2 = c.SensorCo2,
            SensorTvoc = c.SensorTvoc,
            SensorBarometer = c.SensorBarometer,
            SensorPH = c.SensorPH,
            SensorRainLevel = c.SensorRainLevel,
            SensorWaterLevel = c.SensorWaterLevel,
            SensorWind = c.SensorWind,
        };

        // Relay-pin mapping only - Rules/WaterPumpMaxRunSeconds/WaterPumpCooldownSeconds on the DTO come from the assigned zone via DeviceApiController.BuildDeviceConfigAsync, not this row.
        private static DeviceConfigController ToDto(DeviceConfigControllerRow c, IList<DeviceRelaySlot> relays) => new()
        {
            IDDeviceConfigController = c.IDDeviceConfigController,
            RelayEnabled = c.RelayEnabled,
            Relays = relays,
        };

        // ---- Legacy board-less OTA lookup (board-keyed catalog is IFirmwareRepository) --

        public async Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID)
        {
            var row = await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.DeviceTypeID == deviceTypeID)
                .OrderByDescending(f => f.DateAdded)
                .FirstOrDefaultAsync();
            return row == null ? null : EfFirmwareRepository.FirmwareToDto(row);
        }

        // ---- Device diagnostics / fleet ---------------------------------

        public async Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll)
        {
            // "" (every generic esp32dev/esp32s3usbotg build) is normalized to null here rather than resolved to a DeviceTypeID - a device with no specific kit stores no FK at all.
            string? kit = string.IsNullOrEmpty(poll.Kit) ? null : poll.Kit;
            int? deviceTypeId = kit != null ? await EnsureDeviceTypeRegisteredAsync(kit) : null;

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
            row.DeviceTypeID = deviceTypeId ?? row.DeviceTypeID;
            await db.SaveChangesAsync();
        }

        /// deviceDiagnostic.DeviceTypeID has a real FK to deviceType.IDDeviceType - an unrecognized Kit string must never block the device's own heartbeat write because of it, so it's auto-registered here (ControllerCapable=false) in its own save, BEFORE the diagnostic row; a concurrent duplicate insert from another device reporting the same brand-new kit is tolerated by re-fetching the winner's id, not retried.
        private async Task<int> EnsureDeviceTypeRegisteredAsync(string kit)
        {
            int? existingId = await db.DeviceTypes.AsNoTracking().Where(t => t.Kit == kit).Select(t => (int?)t.IDDeviceType).FirstOrDefaultAsync();
            if (existingId is int id)
            {
                return id;
            }
            var candidate = new DeviceTypeRow { Kit = kit, ControllerCapable = false };
            db.DeviceTypes.Add(candidate);
            try
            {
                await db.SaveChangesAsync();
                return candidate.IDDeviceType;
            }
            catch (DbUpdateException ex) when (DbExceptionClassifier.Classify(ex) == DbFailureKind.ConstraintViolation)
            {
                db.Entry(candidate).State = EntityState.Detached; // lost the race - detach so it isn't re-inserted (and re-fails) by the caller's own SaveChangesAsync right after this returns.
                return await db.DeviceTypes.AsNoTracking().Where(t => t.Kit == kit).Select(t => t.IDDeviceType).FirstAsync();
            }
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

        /// A write that changes a device's fleet row (e.g. zone assignment) must drop both its own-tenant and the GlobalAdmin's cached snapshot, or the next Fleet read can still serve the pre-write result for up to FleetCacheTtl. Public (not private) since EfRepository.DeviceUnits.cs (not yet extracted) also calls it after assign/unassign.
        public Task InvalidateFleetCacheAsync(int? tenantID) => Task.WhenAll(
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

            // DeviceType catalog is a small fixed set - cheap to pull entire and check in memory rather than a per-device join.
            Dictionary<int, DeviceTypeRow> deviceTypesById = await db.DeviceTypes.AsNoTracking()
                .ToDictionaryAsync(k => k.IDDeviceType);

            // Units/Zones are a small admin-managed set - same in-memory-lookup reasoning as kitCapability above.
            Dictionary<int, string?> unitNames = await db.DeviceUnits.AsNoTracking()
                .ToDictionaryAsync(u => u.IDDeviceUnit, u => u.DeviceUnitName);
            Dictionary<int, string?> zoneNames = await db.DeviceUnitZones.AsNoTracking()
                .ToDictionaryAsync(z => z.IDDeviceUnitZone, z => z.DeviceUnitZoneName);

            // One bulk read of every relay state for devices in this result set, grouped in memory - same reasoning as kitCapability above (a handful of rows per device, not worth a per-device round trip).
            var deviceIds = rows.Select(r => r.Device.IDDevice).ToList();
            Dictionary<int, List<ControllerDataStatus>> relayStates = (await db.ControllerData.AsNoTracking()
                .Where(c => deviceIds.Contains(c.DeviceID))
                .ToListAsync())
                .GroupBy(c => c.DeviceID)
                .ToDictionary(g => g.Key, g => g.Select(c => new ControllerDataStatus { RelayFunction = (RelayFunction)c.RelayFunction, IsOn = c.IsOn, DateChanged = c.DateChanged }).ToList());

            // One catalog read, newest version per board picked in memory by semver (not DateAdded).
            FirmwareSource activeSource = (await serverConfigRepository.ServerConfigGetAsync(1)).FirmwareSource;
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
                    Kit = r.Diag?.DeviceTypeID is int diagTypeId && deviceTypesById.TryGetValue(diagTypeId, out var diagType) ? diagType.Kit : null,
                    // Admin's explicit DeviceControllerEnabled choice always wins if set - a recognized DeviceType only adds capability, never takes it away. ManualDeviceTypeID is the fallback for a device whose firmware never auto-reports one; the diagnostic-reported DeviceTypeID takes priority whenever both are set.
                    ControllerCapable = r.Device.DeviceControllerEnabled == true
                        || ((r.Diag?.DeviceTypeID ?? r.Device.ManualDeviceTypeID) is int effectiveTypeId && deviceTypesById.TryGetValue(effectiveTypeId, out var effectiveType) && effectiveType.ControllerCapable),
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
                    RelayStates = relayStates.GetValueOrDefault(r.Device.IDDevice),
                };
            }).ToList();
        }

        // ---- Device events -----------------------------------------------

        public async Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message)
        {
            // ServerConfigGetAsync may auto-generate the row (and its EventDedupeMinutes default) on a brand-new install, same as DeviceAddAsync's own call.
            int dedupeMinutes = (await serverConfigRepository.ServerConfigGetAsync(1)).EventDedupeMinutes ?? settings.EventDedupeMinutes;
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

        // ---- Offline alert background worker ------------------------------

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

        // ---- Simulation Mode overrides -------------------------------------

        public async Task<DeviceSimulation?> DeviceSimulationGetAsync(int deviceID)
        {
            var row = await db.DeviceSimulations.AsNoTracking().FirstOrDefaultAsync(s => s.DeviceID == deviceID);
            return row == null ? null : ToDto(row);
        }

        public async Task DeviceSimulationSetAsync(int deviceID, DeviceSimulation value)
        {
            var row = await db.DeviceSimulations.FirstOrDefaultAsync(s => s.DeviceID == deviceID);
            if (row == null)
            {
                row = new DeviceSimulationRow { DeviceID = deviceID };
                db.DeviceSimulations.Add(row);
            }
            row.Enabled = value.Enabled;
            row.Temperature = value.Temperature;
            row.SoilTemperature = value.SoilTemperature;
            row.Humidity = value.Humidity;
            row.Battery = value.Battery;
            row.Moisture = value.Moisture;
            row.Light = value.Light;
            row.Co2 = value.Co2;
            row.Tvoc = value.Tvoc;
            row.Barometer = value.Barometer;
            row.LiquidPH = value.LiquidPH;
            row.RainLevel = value.RainLevel;
            row.WaterLevel = value.WaterLevel;
            row.Wind = value.Wind;
            await db.SaveChangesAsync();
        }

        private static DeviceSimulation ToDto(DeviceSimulationRow s) => new()
        {
            Enabled = s.Enabled,
            Temperature = s.Temperature,
            SoilTemperature = s.SoilTemperature,
            Humidity = s.Humidity,
            Battery = s.Battery,
            Moisture = s.Moisture,
            Light = s.Light,
            Co2 = s.Co2,
            Tvoc = s.Tvoc,
            Barometer = s.Barometer,
            LiquidPH = s.LiquidPH,
            RainLevel = s.RainLevel,
            WaterLevel = s.WaterLevel,
            Wind = s.Wind,
        };
    }
}
