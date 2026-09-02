using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceRepository members (roadmap #74 split, further split by roadmap #95): device
    /// CRUD - configs live in EfRepository.Devices.Config.cs, fixed type lists in
    /// EfRepository.Devices.Types.cs, firmware in EfRepository.Devices.Firmware.cs, and
    /// diagnostics/fleet/events in EfRepository.Devices.Diagnostics.cs.</summary>
    internal partial class EfRepository
    {
        public async Task DeviceAddAsync(Device device)
        {
            // Read (and possibly auto-generate on a brand-new install) BEFORE the transaction below
            // starts - ServerConfigGetAsync's own SaveChangesAsync (if it seeds a row) auto-commits
            // on the shared context (roadmap #101) before BeginTransactionAsync opens this method's
            // own explicit transaction, so the two never nest.
            ServerConfig serverConfig = await ServerConfigGetAsync();

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
                // Roadmap #36: same server-wide-default-then-per-device-override rule as the
                // hysteresis fields above.
                WaterPumpMaxRunSeconds = serverConfig.WaterPumpMaxRunSeconds,
                WaterPumpCooldownSeconds = serverConfig.WaterPumpCooldownSeconds,
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

        // #66 Phase 2: same query minus the tenant filter - callers (DeviceApiController) only
        // reach this after CallerReadsDevicesGlobally passed, mirroring UsersGetAllAsync.
        public async Task<IList<Device>> DevicesGetAllAsync()
        {
            var rows = await db.Devices.AsNoTracking().ToListAsync();
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

            // Columns the DeviceUpdate proc touched (note: it did NOT set MacAddress or the
            // config-id columns). Roadmap #82: DeviceUnitID/DeviceUnitZoneID dropped from this list
            // too - both are now written exclusively by DeviceAssignToZoneAsync/
            // DeviceUnassignFromZoneAsync, which keep the pair consistent with each other (derived
            // from the zone's own DeviceUnitID); this generic update touching just one of them could
            // silently desync a device's Unit from its Zone.
            row.TenantID = device.TenantID;
            row.DeviceTypeID = device.DeviceTypeID;
            row.DeviceTypeServiceID = device.DeviceTypeServiceID;
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
        };
    }
}
