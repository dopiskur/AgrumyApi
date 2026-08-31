using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceRepository members (roadmap #74 split): device CRUD, configs, firmware,
    /// fixed type lists, and device events (roadmap #28).</summary>
    internal partial class EfRepository
    {
        public async Task DeviceAddAsync(Device device)
        {
            // Outside the transaction below: read-only, and ServerConfigGetAsync opens its own
            // connection via Db() - auto-generates the row on a brand-new install.
            ServerConfig serverConfig = await ServerConfigGetAsync();

            await using var db = Db();
            await using var tx = await db.Database.BeginTransactionAsync();

            var sensorCfg = new DeviceConfigSensorRow();
            var controllerCfg = new DeviceConfigControllerRow
            {
                // Hysteresis starts at the server-wide default; admin can override per device
                // afterwards under Device -> Controller.
                WaterLevelHysteresis = serverConfig.WaterLevelHysteresis,
                TemperatureHysteresis = serverConfig.TemperatureHysteresis,
                HumidityHysteresis = serverConfig.HumidityHysteresis,
                LightHysteresis = serverConfig.LightHysteresis,
            };
            db.DeviceConfigSensors.Add(sensorCfg);
            db.DeviceConfigControllers.Add(controllerCfg);
            await db.SaveChangesAsync();

            db.Devices.Add(new DeviceRow
            {
                TenantID = device.TenantID,
                DeviceTypeID = device.DeviceTypeID,
                DeviceUnitID = device.DeviceUnitID,
                DeviceUnitZoneID = device.DeviceUnitZoneID,
                DeviceName = device.DeviceName,
                MacAddress = device.MacAddress,
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
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task DeviceDeleteAsync(int? idDevice, int? tenantID)
        {
            await using var db = Db();
            var target = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice && d.TenantID == tenantID)
                .Select(d => new { d.DeviceConfigSensorID, d.DeviceConfigControllerID })
                .FirstOrDefaultAsync();
            if (target == null)
            {
                return;
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            // Diagnostics first: its FK to device is NoAction, so leaving the row would block the delete.
            await db.DeviceDiagnostics.Where(x => x.DeviceID == idDevice).ExecuteDeleteAsync();
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
            await using var db = Db();
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
            await using var db = Db();
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByApiIdAsync(string? apiId)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiId == apiId);
            return row == null ? null : ToDto(row);
        }

        public async Task<IList<Device>> DevicesGetAsync(int? tenantID)
        {
            await using var db = Db();
            var rows = await db.Devices.AsNoTracking().Where(d => d.TenantID == tenantID).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // #66 Phase 2: same query minus the tenant filter - callers (DeviceApiController) only
        // reach this after CallerReadsDevicesGlobally passed, mirroring UsersGetAllAsync.
        public async Task<IList<Device>> DevicesGetAllAsync()
        {
            await using var db = Db();
            var rows = await db.Devices.AsNoTracking().ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress)
        {
            await using var db = Db();
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.TenantID == tenantID && d.MacAddress == macAddress);
        }

        public async Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID)
        {
            await using var db = Db();
            var row = await db.DeviceConfigSensors.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID)
        {
            await using var db = Db();
            var row = await db.DeviceConfigControllers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigSensorID == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigControllerID == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID)
        {
            await using var db = Db();
            return await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.DeviceTypeID == deviceTypeID)
                .OrderByDescending(f => f.DateAdded)
                .Select(f => new DeviceFirmware
                {
                    IDDeviceFirmware = f.IDDeviceFirmware,
                    DeviceTypeID = f.DeviceTypeID,
                    Version = f.Version,
                    Url = f.Url,
                    DateAdded = f.DateAdded,
                })
                .FirstOrDefaultAsync();
        }

        public async Task DeviceUpdateAsync(Device? device)
        {
            if (device == null)
            {
                return;
            }

            await using var db = Db();
            var row = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == device.IDDevice);
            if (row == null)
            {
                return;
            }

            // Columns the DeviceUpdate proc touched (note: it did NOT set DeviceUnitZoneID,
            // MacAddress or the config-id columns).
            row.TenantID = device.TenantID;
            row.DeviceTypeID = device.DeviceTypeID;
            row.DeviceTypeServiceID = device.DeviceTypeServiceID;
            row.DeviceUnitID = device.DeviceUnitID;
            row.DeviceName = device.DeviceName;
            row.ApiId = device.ApiId ?? "";
            row.ApiKey = device.ApiKey ?? "";
            row.ServicePoint = device.ServicePoint;
            row.ServicePublicKey = device.ServicePublicKey;
            row.SleepSeconds = device.SleepSeconds;
            row.SleepDeepEnabled = device.SleepDeepEnabled;
            row.DeviceSensorEnabled = device.DeviceSensorEnabled;
            row.DeviceControllerEnabled = device.DeviceControllerEnabled;
            row.BatteryEnabled = device.BatteryEnabled;
            row.Enabled = device.Enabled;
            row.Debug = device.Debug;
            row.ConfigVersion = (device.ConfigVersion ?? 0) + 1; // proc: ConfigVersion = configVersion + 1
            await db.SaveChangesAsync();
        }

        public async Task DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            await using var db = Db();

            var row = await db.DeviceConfigControllers
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == cfg.IDDeviceConfigController);
            if (row != null)
            {
                // The proc declared these params as int (columns are double) so historically the
                // values were truncated. Phase 1 stores the real double instead - a deliberate,
                // documented deviation from the proc.
                row.TempLow = cfg.TempLow;
                row.TempHigh = cfg.TempHigh;
                row.HumidLow = cfg.HumidLow;
                row.HumidHigh = cfg.HumidHigh;
                row.MoistLow = cfg.MoistLow;
                row.MoistHigh = cfg.MoistHigh;
                row.LightLow = cfg.LightLow;
                row.LightHigh = cfg.LightHigh;
                row.WaterLow = cfg.WaterLow;
                row.WaterHigh = cfg.WaterHigh;
                row.WaterLevelHysteresis = cfg.WaterLevelHysteresis;
                row.TemperatureHysteresis = cfg.TemperatureHysteresis;
                row.HumidityHysteresis = cfg.HumidityHysteresis;
                row.LightHysteresis = cfg.LightHysteresis;
                row.VentilationIntervalEnabled = cfg.VentilationIntervalEnabled;
                row.VentilationInterval = cfg.VentilationInterval;
                row.VentilationIntervalLenght = cfg.VentilationIntervalLenght;
                row.LightIntervalEnabled = cfg.LightIntervalEnabled;
                row.LightInterval = cfg.LightInterval;
                row.LightIntervalLenght = cfg.LightIntervalLenght;
                row.HeatingIntervalEnabled = cfg.HeatingIntervalEnabled;
                row.HeatingInterval = cfg.HeatingInterval;
                row.HeatingIntervalLenght = cfg.HeatingIntervalLenght;
                row.WaterPumpIntervalEnabled = cfg.WaterPumpIntervalEnabled;
                row.WaterPumpInterval = cfg.WaterPumpInterval;
                row.WaterPumpIntervalLenght = cfg.WaterPumpIntervalLenght;
                row.RelayEnabled = cfg.RelayEnabled;
                row.Relay1 = cfg.Relay1;
                row.Relay2 = cfg.Relay2;
                row.Relay3 = cfg.Relay3;
                row.Relay4 = cfg.Relay4;
                row.Relay5 = cfg.Relay5;
                row.Relay6 = cfg.Relay6;
                row.Relay7 = cfg.Relay7;
                row.Relay8 = cfg.Relay8;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        public async Task DeviceConfigSensorUpdateAsync(int? idDevice, DeviceConfigSensor? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            await using var db = Db();

            var row = await db.DeviceConfigSensors
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == cfg.IDDeviceConfigSensor);
            if (row != null)
            {
                row.SensorBattery = cfg.SensorBattery;
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

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        public async Task<IList<DeviceType>> DeviceTypeGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypes.AsNoTracking()
                .Select(t => new DeviceType
                {
                    IDDeviceType = t.IDDeviceType,
                    DeviceTypeName = t.DeviceTypeName,
                    SensorEnabled = t.SensorEnabled,
                    ControllerEnabled = t.ControllerEnabled,
                })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypeServices.AsNoTracking()
                .Select(s => new DeviceTypeService { IDDeviceTypeService = s.IDDeviceTypeService, ServiceType = s.ServiceType })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypeRelays.AsNoTracking()
                .Select(r => new DeviceTypeRelay { IDDeviceTypeRelay = r.IDDeviceTypeRelay, RelayName = r.RelayName })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync()
        {
            await using var db = Db();
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

        // ---- Device diagnostics / fleet (roadmap #7 + #8) --------------------------

        public async Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll)
        {
            await using var db = Db();
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
            await using var db = Db();
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
            }).ToList();
        }

        // ---- Device events (roadmap #28) -------------------------------------------

        public async Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message)
        {
            // Read outside the write connection, same reasoning as DeviceAddAsync's ServerConfigGetAsync
            // call - auto-generates the row (and its EventDedupeMinutes default) on a brand-new install.
            int dedupeMinutes = (await ServerConfigGetAsync()).EventDedupeMinutes ?? Config.eventDedupeMinutes;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-dedupeMinutes);

            await using var db = Db();

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
            await using var db = Db();
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
            await using var db = Db();
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
            await using var db = Db();
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

        private static Device ToDto(DeviceRow d) => new()
        {
            IDDevice = d.IDDevice,
            TenantID = d.TenantID,
            DeviceTypeID = d.DeviceTypeID,
            DeviceUnitID = d.DeviceUnitID,
            DeviceUnitZoneID = d.DeviceUnitZoneID,
            DeviceConfigSensorID = d.DeviceConfigSensorID,
            DeviceConfigControllerID = d.DeviceConfigControllerID,
            DeviceTypeServiceID = d.DeviceTypeServiceID,
            DeviceName = d.DeviceName,
            MacAddress = d.MacAddress,
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
            Enabled = d.Enabled,
            ConfigVersion = d.ConfigVersion,
            DateCreated = d.DateCreated,
            DateModified = d.DateModified,
        };

        private static DeviceConfigSensor ToDto(DeviceConfigSensorRow c) => new()
        {
            IDDeviceConfigSensor = c.IDDeviceConfigSensor,
            SensorBattery = c.SensorBattery,
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

        private static DeviceConfigController ToDto(DeviceConfigControllerRow c) => new()
        {
            IDDeviceConfigController = c.IDDeviceConfigController,
            TempLow = c.TempLow,
            TempHigh = c.TempHigh,
            HumidLow = c.HumidLow,
            HumidHigh = c.HumidHigh,
            MoistLow = c.MoistLow,
            MoistHigh = c.MoistHigh,
            LightLow = c.LightLow,
            LightHigh = c.LightHigh,
            WaterLow = c.WaterLow,
            WaterHigh = c.WaterHigh,
            WaterLevelHysteresis = c.WaterLevelHysteresis,
            TemperatureHysteresis = c.TemperatureHysteresis,
            HumidityHysteresis = c.HumidityHysteresis,
            LightHysteresis = c.LightHysteresis,
            VentilationIntervalEnabled = c.VentilationIntervalEnabled,
            VentilationInterval = c.VentilationInterval,
            VentilationIntervalLenght = c.VentilationIntervalLenght,
            LightIntervalEnabled = c.LightIntervalEnabled,
            LightInterval = c.LightInterval,
            LightIntervalLenght = c.LightIntervalLenght,
            HeatingIntervalEnabled = c.HeatingIntervalEnabled,
            HeatingInterval = c.HeatingInterval,
            HeatingIntervalLenght = c.HeatingIntervalLenght,
            WaterPumpIntervalEnabled = c.WaterPumpIntervalEnabled,
            WaterPumpInterval = c.WaterPumpInterval,
            WaterPumpIntervalLenght = c.WaterPumpIntervalLenght,
            RelayEnabled = c.RelayEnabled,
            Relay1 = c.Relay1,
            Relay2 = c.Relay2,
            Relay3 = c.Relay3,
            Relay4 = c.Relay4,
            Relay5 = c.Relay5,
            Relay6 = c.Relay6,
            Relay7 = c.Relay7,
            Relay8 = c.Relay8,
        };
    }
}
