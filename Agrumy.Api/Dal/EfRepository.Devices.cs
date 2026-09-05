using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDeviceRepository members: device CRUD - configs, type lists, firmware, and diagnostics/fleet/events live in the other EfRepository.Devices.*.cs partials.
    internal partial class EfRepository
    {
        public async Task<Device> DeviceAddAsync(Device device)
        {
            // Read (and possibly auto-generate) BEFORE the transaction below starts - ServerConfigGetAsync's own seed SaveChangesAsync must auto-commit before this method's explicit transaction opens, so the two never nest.
            ServerConfig serverConfig = await ServerConfigGetAsync();

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

        // Same query minus the tenant filter - callers (DeviceApiController) only reach this after
        // CallerReadsDevicesGlobally passed, mirroring UsersGetAllAsync.
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

            // Does not set MacAddress, config-id columns, DeviceUnitID/DeviceUnitZoneID (written exclusively by DeviceAssignToZoneAsync/DeviceUnassignFromZoneAsync to stay consistent), or ApiId/ApiKey (omitting them used to wipe a device's real credential).
            row.TenantID = device.TenantID;
            row.DeviceTypeID = device.DeviceTypeID;
            row.DeviceTypeServiceID = device.DeviceTypeServiceID;
            row.DeviceName = device.DeviceName;
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
    }
}
