using api.Dal.Interface;
using api.Firmware;
using api.Models;
using api.Utils;

namespace api.Devices
{
    /// Builds the DeviceConfig body a Config poll or Register response sends back, shared so RelayApiController.Batch's Config entries produce byte-for-byte the same response as a direct POST /api/Device/Config.
    public class DeviceConfigBuilder(IRepository repo, FirmwareCatalogService firmwareCatalog)
    {
        /// Whether GetConfig/RunConfigAsync must send a full config this poll: a real version mismatch, a pending command, or - because BuildAsync recomputes UtcOffsetSeconds/SkipWaterPumpForRain fresh every call without either ever bumping ConfigVersion - the periodic heartbeat window has elapsed since the device's last full send. Not used by Register, which always sends a fresh config unconditionally.
        public async Task<bool> NeedsRefreshAsync(Device device, int? pollConfigVersion, PendingCommand? pendingCommand)
        {
            if (pollConfigVersion != device.ConfigVersion || pendingCommand != null)
            {
                return true;
            }
            ServerConfig serverConfig = await repo.ServerConfigGetAsync(1);
            if (serverConfig.ConfigHeartbeatHours <= 0)
            {
                return false;
            }
            return device.LastFullConfigSentAt is not DateTime last
                || (DateTime.UtcNow - last).TotalHours >= serverConfig.ConfigHeartbeatHours;
        }

        public async Task<DeviceConfig> BuildAsync(Device device, PendingCommand? pendingCommand, string? board)
        {
            // Computed fresh on every Config/Register response rather than cached, so a DST
            // transition or an admin changing the tenant's ScheduleTimeZone reaches every device on
            // its very next poll (subject to NeedsRefreshAsync's own heartbeat gate). ServerConfig is
            // still fetched here and reused for WeatherRainPredicted below.
            ServerConfig serverConfig = await repo.ServerConfigGetAsync(1);
            // Per-tenant, not global - a device with no tenant row (shouldn't happen) or an unset zone both fall back to UTC via GetUtcOffsetSeconds' own null handling.
            Tenant? tenant = await repo.TenantGetByIdAsync(device.TenantID);
            int utcOffsetSeconds = TimeZoneHelper.GetUtcOffsetSeconds(DateTime.UtcNow, tenant?.ScheduleTimeZone);

            var deviceConfig = new DeviceConfig
            {
                ConfigVersion = device.ConfigVersion,
                TenantID = device.TenantID,
                deviceID = device.IDDevice,
                DeviceUnitID = device.DeviceUnitID,
                DeviceUnitZoneID = device.DeviceUnitZoneID,
                ApiId = device.ApiId,
                ApiKey = device.ApiKey,
                ServicePoint = device.ServicePoint,
                DeviceTypeServiceID = device.DeviceTypeServiceID,
                ServicePublicKey = device.ServicePublicKey,
                UtcOffsetSeconds = utcOffsetSeconds,
                DeviceSensorEnabled = device.DeviceSensorEnabled,
                DeviceControllerEnabled = device.DeviceControllerEnabled,
                BatteryEnabled = device.BatteryEnabled,
                Debug = device.Debug,
                Reboot = device.Reboot,
                Reset = device.Reset,
                FirmwareUpdate = device.FirmwareUpdate,
                Enabled = device.Enabled,
                CommandVersion = device.CommandVersion,
                PendingCommand = pendingCommand,
            };

            // The firmware does a version comparison of its own, so an offer being present on every
            // Config sync is fine - harmless on Register too, since a freshly-created device has
            // FirmwareUpdate == null and ResolveOfferAsync returns null for that.
            DeviceFirmware? firmware = await firmwareCatalog.ResolveOfferAsync(device, board);
            if (firmware != null)
            {
                deviceConfig.FirmwareVersion = firmware.Version;
                deviceConfig.FirmwareUrl = firmware.Url;
                deviceConfig.FirmwareSha256 = firmware.Sha256;
            }

            if (deviceConfig.DeviceSensorEnabled == true)
            {
                deviceConfig.DeviceConfigSensor = await repo.DeviceConfigSensorGetAsync(device.DeviceConfigSensorID);
            }
            if (deviceConfig.DeviceControllerEnabled == true)
            {
                // Relay-pin mapping still comes from the device's own row, but Rules and safety
                // limits come from whichever zone the device is assigned to - merged into the SAME
                // DeviceConfigController object the firmware already expects. No zone assigned
                // means an empty Rules list, so every relay function simply stays off.
                DeviceConfigController? controller = await repo.DeviceConfigControllerGetAsync(device.DeviceConfigControllerID);
                if (controller != null && device.DeviceUnitZoneID is int idZone)
                {
                    controller.Rules = await repo.DeviceUnitZoneRulesGetAsync(idZone);
                    DeviceUnitZone? zone = await repo.DeviceUnitZoneGetByIdAsync(idZone);
                    controller.WaterPumpMaxRunSeconds = zone?.WaterPumpMaxRunSeconds;
                    controller.WaterPumpCooldownSeconds = zone?.WaterPumpCooldownSeconds;
                    // Computed here as a single AND-NOT gate, not sent as two separate flags - see
                    // DeviceConfigController.SkipWaterPumpForRain's remarks.
                    controller.SkipWaterPumpForRain = zone?.SkipWaterPumpWhenRainPredicted == true && serverConfig.WeatherRainPredicted;
                }
                deviceConfig.DeviceConfigController = controller;
            }

            return deviceConfig;
        }
    }
}
