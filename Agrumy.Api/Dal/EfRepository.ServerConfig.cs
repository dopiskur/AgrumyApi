using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IServerConfigRepository members.
    internal partial class EfRepository
    {
        public async Task<ServerConfig> ServerConfigGetAsync(int idServerConfig = 1)
        {
            var row = await db.ServerConfigs.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);

            if (row != null)
            {
                return ToDto(row);
            }

            // No row: generate one, seeding hysteresis fields from appsettings.json so a fresh install starts with sane defaults.
            var generated = new ServerConfigRow
            {
                IDServerConfig = idServerConfig,
                ServerConfigName = "DefaultGenerated" + idServerConfig,
                ConfigKey = Guid.NewGuid().ToString(),
                PortHTTP = 80,
                PortHTTPS = 443,
                WaterLevelHysteresis = settings.HysteresisWaterLevel,
                TemperatureHysteresis = settings.HysteresisTemperature,
                HumidityHysteresis = settings.HysteresisHumidity,
                LightHysteresis = settings.HysteresisLight,
                BatteryLowThreshold = settings.BatteryLowThreshold,
                BatteryLowHysteresis = settings.BatteryLowHysteresis,
                WaterPumpMaxRunSeconds = settings.WaterPumpMaxRunSeconds,
                WaterPumpCooldownSeconds = settings.WaterPumpCooldownSeconds,
                EventDedupeMinutes = settings.EventDedupeMinutes,
                ActivationResendCooldownMinutes = settings.ActivationResendCooldownMinutes,
                MaxRulesPerZone = settings.MaxRulesPerZone,
                AllowSelfServiceTenantCreation = settings.AllowSelfServiceTenantCreation,
                TenantManagementEnabled = settings.TenantManagementEnabled,
                FirmwareSource = (int)FirmwareSource.GitHub,
                FirmwareGitHubRepository = settings.FirmwareGitHubRepository,
                FirmwareRefreshIntervalHours = settings.FirmwareRefreshIntervalHours,
                SensorDataRetentionDays = settings.SensorDataRetentionDays,
                WeatherPollIntervalMinutes = settings.WeatherPollIntervalMinutes,
                WeatherRainSkipThreshold = settings.WeatherRainSkipThreshold,
                GatewayWaitWindowSeconds = 30,
                ProblemEventAlertsEnabled = true,
                ProblemEventExpiryHours = 24,
                PasswordMinLength = 8,
                ConfigHeartbeatHours = 24,
            };
            db.ServerConfigs.Add(generated);
            await db.SaveChangesAsync();
            return ToDto(generated);
        }

        public async Task ServerConfigUpdateAsync(ServerConfig config)
        {
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == config.IDServerConfig);
            if (row == null)
            {
                return;
            }

            row.WaterLevelHysteresis = config.WaterLevelHysteresis;
            row.TemperatureHysteresis = config.TemperatureHysteresis;
            row.HumidityHysteresis = config.HumidityHysteresis;
            row.LightHysteresis = config.LightHysteresis;
            row.BatteryLowThreshold = config.BatteryLowThreshold;
            row.BatteryLowHysteresis = config.BatteryLowHysteresis;
            row.WaterPumpMaxRunSeconds = config.WaterPumpMaxRunSeconds;
            row.WaterPumpCooldownSeconds = config.WaterPumpCooldownSeconds;
            row.EventDedupeMinutes = config.EventDedupeMinutes;
            row.ActivationResendCooldownMinutes = config.ActivationResendCooldownMinutes;
            row.MaxRulesPerZone = config.MaxRulesPerZone;
            row.AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation;
            row.TenantManagementEnabled = config.TenantManagementEnabled;
            row.FirmwareSource = (int)config.FirmwareSource;
            row.FirmwareGitHubRepository = config.FirmwareGitHubRepository;
            row.FirmwareCustomRepositoryUrl = config.FirmwareCustomRepositoryUrl;
            row.FirmwareRefreshIntervalHours = config.FirmwareRefreshIntervalHours;
            // FirmwareLastRefreshedAtUtc deliberately NOT written here - see ServerConfigFirmwareRefreshStateSetAsync below.
            row.SensorDataRetentionDays = config.SensorDataRetentionDays;
            // WeatherRainPredicted/WeatherCheckedAtUtc deliberately NOT written here - WeatherEvaluator owns them via ServerConfigWeatherStateSetAsync, so a form post can't clobber a fresher reading.
            row.WeatherLocationLat = config.WeatherLocationLat;
            row.WeatherLocationLon = config.WeatherLocationLon;
            row.WeatherPollIntervalMinutes = config.WeatherPollIntervalMinutes;
            row.WeatherRainSkipThreshold = config.WeatherRainSkipThreshold;
            row.GatewayEnabled = config.GatewayEnabled;
            row.GatewayMode = (int)config.GatewayMode;
            row.GatewayWaitWindowSeconds = config.GatewayWaitWindowSeconds;
            row.ProblemEventAlertsEnabled = config.ProblemEventAlertsEnabled;
            row.ProblemEventExpiryHours = config.ProblemEventExpiryHours;
            row.PasswordMinLength = config.PasswordMinLength;
            row.PasswordRequireComplexity = config.PasswordRequireComplexity;
            row.ConfigHeartbeatHours = config.ConfigHeartbeatHours;
            await db.SaveChangesAsync();

            // Re-applied on every save so Postgres/TimescaleDB retention updates immediately - a no-op on MariaDB/MySQL, which reads this row fresh on its own daily tick.
            await ApplyRetentionPolicyAsync(config.SensorDataRetentionDays);
        }

        /// Forces hysteresis fields back to appsettings.json's ServerConfig:Hysteresis values (creating the row if missing) - only called at startup when AgrumySettings.ServerConfigReload is true.
        public async Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig = 1)
        {
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);
            if (row == null)
            {
                row = new ServerConfigRow
                {
                    IDServerConfig = idServerConfig,
                    ServerConfigName = "DefaultGenerated" + idServerConfig,
                    ConfigKey = Guid.NewGuid().ToString(),
                    PortHTTP = 80,
                    PortHTTPS = 443,
                };
                db.ServerConfigs.Add(row);
            }

            row.WaterLevelHysteresis = settings.HysteresisWaterLevel;
            row.TemperatureHysteresis = settings.HysteresisTemperature;
            row.HumidityHysteresis = settings.HysteresisHumidity;
            row.LightHysteresis = settings.HysteresisLight;
            row.BatteryLowThreshold = settings.BatteryLowThreshold;
            row.BatteryLowHysteresis = settings.BatteryLowHysteresis;
            row.WaterPumpMaxRunSeconds = settings.WaterPumpMaxRunSeconds;
            row.WaterPumpCooldownSeconds = settings.WaterPumpCooldownSeconds;
            row.EventDedupeMinutes = settings.EventDedupeMinutes;
            row.ActivationResendCooldownMinutes = settings.ActivationResendCooldownMinutes;
            row.MaxRulesPerZone = settings.MaxRulesPerZone;
            row.AllowSelfServiceTenantCreation = settings.AllowSelfServiceTenantCreation;
            row.TenantManagementEnabled = settings.TenantManagementEnabled;
            row.SensorDataRetentionDays = settings.SensorDataRetentionDays;
            row.WeatherPollIntervalMinutes = settings.WeatherPollIntervalMinutes;
            row.WeatherRainSkipThreshold = settings.WeatherRainSkipThreshold;
            row.FirmwareRefreshIntervalHours = settings.FirmwareRefreshIntervalHours;
            await db.SaveChangesAsync();
            await ApplyRetentionPolicyAsync(settings.SensorDataRetentionDays);
        }

        /// The only writer of WeatherRainPredicted/WeatherCheckedAtUtc, called exclusively by WeatherEvaluator - narrower than ServerConfigUpdateAsync so the admin form can't race a fresher reading back to stale.
        public async Task ServerConfigWeatherStateSetAsync(bool rainPredicted, DateTime checkedAtUtc, int idServerConfig = 1)
        {
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);
            if (row == null)
            {
                return; // no row yet - nothing meaningful to attach this to, next GetAsync seeds one
            }
            row.WeatherRainPredicted = rainPredicted;
            row.WeatherCheckedAtUtc = checkedAtUtc;
            await db.SaveChangesAsync();
        }

        /// The only writer of FirmwareLastRefreshedAtUtc, called exclusively by FirmwareCatalogRefreshEvaluator - same isolation reasoning as ServerConfigWeatherStateSetAsync.
        public async Task ServerConfigFirmwareRefreshStateSetAsync(DateTime checkedAtUtc, int idServerConfig = 1)
        {
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);
            if (row == null)
            {
                return;
            }
            row.FirmwareLastRefreshedAtUtc = checkedAtUtc;
            await db.SaveChangesAsync();
        }

        // Instance, not static - the GitHub-repository fallback below reads the injected settings.
        private ServerConfig ToDto(ServerConfigRow r) => new()
        {
            IDServerConfig = r.IDServerConfig,
            ServerConfigName = r.ServerConfigName,
            ConfigKey = r.ConfigKey,
            PortHTTP = r.PortHTTP,
            PortHTTPS = r.PortHTTPS,
            WaterLevelHysteresis = r.WaterLevelHysteresis,
            TemperatureHysteresis = r.TemperatureHysteresis,
            HumidityHysteresis = r.HumidityHysteresis,
            LightHysteresis = r.LightHysteresis,
            BatteryLowThreshold = r.BatteryLowThreshold,
            BatteryLowHysteresis = r.BatteryLowHysteresis,
            WaterPumpMaxRunSeconds = r.WaterPumpMaxRunSeconds,
            WaterPumpCooldownSeconds = r.WaterPumpCooldownSeconds,
            EventDedupeMinutes = r.EventDedupeMinutes,
            ActivationResendCooldownMinutes = r.ActivationResendCooldownMinutes,
            MaxRulesPerZone = r.MaxRulesPerZone ?? settings.MaxRulesPerZone,
            AllowSelfServiceTenantCreation = r.AllowSelfServiceTenantCreation,
            TenantManagementEnabled = r.TenantManagementEnabled,
            FirmwareSource = (FirmwareSource)r.FirmwareSource,
            // An older row has NULL here; falling back to the settings seed beats an empty repository nobody can sync from.
            FirmwareGitHubRepository = string.IsNullOrWhiteSpace(r.FirmwareGitHubRepository) ? settings.FirmwareGitHubRepository : r.FirmwareGitHubRepository,
            FirmwareCustomRepositoryUrl = r.FirmwareCustomRepositoryUrl,
            // An older row has NULL here - same appsettings-seed fallback as FirmwareGitHubRepository, rather than showing "disabled" for every existing install.
            FirmwareRefreshIntervalHours = r.FirmwareRefreshIntervalHours ?? settings.FirmwareRefreshIntervalHours,
            FirmwareLastRefreshedAtUtc = r.FirmwareLastRefreshedAtUtc,
            SensorDataRetentionDays = r.SensorDataRetentionDays,
            WeatherLocationLat = r.WeatherLocationLat,
            WeatherLocationLon = r.WeatherLocationLon,
            // An older row has NULL here - same appsettings-seed fallback as FirmwareGitHubRepository, rather than surfacing an empty interval/threshold.
            WeatherPollIntervalMinutes = r.WeatherPollIntervalMinutes ?? settings.WeatherPollIntervalMinutes,
            WeatherRainSkipThreshold = r.WeatherRainSkipThreshold ?? settings.WeatherRainSkipThreshold,
            WeatherRainPredicted = r.WeatherRainPredicted,
            WeatherCheckedAtUtc = r.WeatherCheckedAtUtc,
            GatewayEnabled = r.GatewayEnabled,
            GatewayMode = (GatewayMode)r.GatewayMode,
            // An older row has 0 here, which already equals the sane default (a 10-300 clamp keeps 0 unreachable otherwise) - no settings.* fallback needed.
            GatewayWaitWindowSeconds = r.GatewayWaitWindowSeconds == 0 ? 30 : r.GatewayWaitWindowSeconds,
            ProblemEventAlertsEnabled = r.ProblemEventAlertsEnabled,
            // An older row has 0 here (column default backfills real rows to 24; the generated-default path above already sets it explicitly).
            ProblemEventExpiryHours = r.ProblemEventExpiryHours == 0 ? 24 : r.ProblemEventExpiryHours,
            // An older row has 0 here, which is not a usable minimum length - same 0-means-unset fallback as GatewayWaitWindowSeconds.
            PasswordMinLength = r.PasswordMinLength == 0 ? 8 : r.PasswordMinLength,
            PasswordRequireComplexity = r.PasswordRequireComplexity,
            ConfigHeartbeatHours = r.ConfigHeartbeatHours,
        };
    }
}
