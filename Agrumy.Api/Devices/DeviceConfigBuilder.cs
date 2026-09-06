using api.Dal.Interface;
using api.Firmware;
using api.Models;
using api.Utils;

namespace api.Devices
{
    /// Builds the DeviceConfig body a Config poll or Register response sends back, shared so GatewayApiController.Batch's Config entries produce byte-for-byte the same response as a direct POST /api/Device/Config.
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
            // Computed fresh (not cached) every response so a DST shift or ScheduleTimeZone change reaches every device on its next poll; also reused below for WeatherRainPredicted.
            ServerConfig serverConfig = await repo.ServerConfigGetAsync(1);
            // Per-tenant, not global - a device with no tenant row (shouldn't happen) or an unset zone both fall back to UTC via GetUtcOffsetSeconds' own null handling.
            Tenant? tenant = await repo.TenantGetByIdAsync(device.TenantID);
            int utcOffsetSeconds = TimeZoneHelper.GetUtcOffsetSeconds(DateTime.UtcNow, tenant?.ScheduleTimeZone);

            var deviceConfig = new DeviceConfig
            {
                ConfigVersion = device.ConfigVersion,
                TenantID = device.TenantID,
                deviceID = device.IDDevice,
                DeviceFarmUnitID = device.DeviceFarmUnitID,
                DeviceFarmUnitZoneID = device.DeviceFarmUnitZoneID,
                ApiId = device.ApiId,
                ApiKey = device.ApiKey,
                ServicePoint = device.ServicePoint,
                DeviceTypeServiceID = device.DeviceTypeServiceID,
                ServicePublicKey = device.ServicePublicKey,
                SleepSeconds = device.SleepSeconds,
                SleepDeep = device.SleepDeepEnabled,
                LoRaGatewayEnabled = device.LoRaGatewayEnabled,
                UtcOffsetSeconds = utcOffsetSeconds,
                ServerUtcEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                DeviceSensorEnabled = device.DeviceSensorEnabled,
                DeviceControllerEnabled = device.DeviceControllerEnabled,
                BatteryEnabled = device.BatteryEnabled,
                Debug = device.Debug,
                Reboot = device.Reboot,
                Reset = device.Reset,
                FirmwareUpdate = device.FirmwareUpdate,
                Enabled = device.Enabled,
                EmergencyStop = tenant?.EmergencyStopActive == true,
                CommandVersion = device.CommandVersion,
                PendingCommand = pendingCommand,
                SimulationModeEnabled = (await repo.DeviceSimulationGetAsync(device.IDDevice!.Value))?.Enabled == true,
            };

            // Fire-once, cleared the instant it's included rather than waiting for a confirmation that can never come back - a device told to reset() wipes itself and restarts before it could ever report anything, so "wait for the device to confirm" (FirmwareUpdate's pattern) would leave this stuck true and re-trigger on every future poll after the device re-registers.
            if (device.Reset == true)
            {
                await repo.DeviceHardResetSetAsync(device.IDDevice!.Value, false);
            }

            // Firmware compares versions itself, so an offer present on every Config sync is fine, and harmless on Register too since ResolveOfferAsync returns null for a freshly-created device.
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
                // Relay-pin mapping comes from the device row, but Rules/safety limits come from its zone, merged into the same DeviceConfigController; no zone means an empty Rules list so every relay stays off.
                DeviceConfigController? controller = await repo.DeviceConfigControllerGetAsync(device.DeviceConfigControllerID);
                if (controller != null && device.DeviceFarmUnitZoneID is int idZone)
                {
                    IList<DeviceFarmUnitZoneRule> zoneRules = await repo.RulesGetForZoneAsync(idZone);
                    IList<DeviceFarmUnitZoneRule> unitRules = device.DeviceFarmUnitID is int idUnit ? await repo.RulesGetForUnitAsync(idUnit) : [];
                    IList<DeviceFarmUnitZoneRule> globalRules = await repo.RulesGetForTenantGlobalAsync(device.TenantID);
                    IList<DeviceFarmUnitZoneRule> rules = RuleHierarchyResolver.ResolveRelayRules(zoneRules, unitRules, globalRules);
                    DateOnly localDate = DateOnly.FromDateTime(DateTime.UtcNow.AddSeconds(utcOffsetSeconds));
                    controller.Rules = AstronomicalRuleResolver.Resolve(rules, serverConfig, localDate, utcOffsetSeconds);
                    DeviceFarmUnitZone? zone = await repo.DeviceFarmUnitZoneGetByIdAsync(idZone);
                    controller.WaterPumpMaxRunSeconds = zone?.WaterPumpMaxRunSeconds;
                    controller.WaterPumpCooldownSeconds = zone?.WaterPumpCooldownSeconds;
                    // Computed here as a single AND-NOT gate, not sent as two separate flags - see DeviceConfigController.SkipWaterPumpForRain's remarks.
                    controller.SkipWaterPumpForRain = zone?.SkipWaterPumpWhenRainPredicted == true && serverConfig.WeatherRainPredicted;

                    // Roadmap #219 - only what's still active (not yet past ExpiresAtUtc) rides along; a naturally-expired command simply stops appearing on the next poll, no explicit "stop" needed.
                    IList<DeviceManualOverride> activeOverrides = await repo.ManualOverridesActiveForDeviceAsync(device.IDDevice!.Value);
                    controller.ManualOverrides = activeOverrides.Select(o => new DeviceManualOverridePush
                    {
                        RelayFunction = o.RelayFunction,
                        Mode = o.Mode,
                        ExpiresAtEpoch = ((DateTimeOffset)DateTime.SpecifyKind(o.ExpiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                        TargetMetric = o.TargetMetric,
                        TargetThreshold = o.TargetThreshold,
                        TargetHysteresis = o.TargetHysteresis,
                    }).ToList();
                }
                deviceConfig.DeviceConfigController = controller;
            }

            return deviceConfig;
        }
    }
}
