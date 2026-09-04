using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IServerConfigRepository members.</summary>
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

            // No row: generate a default one (mirrors the old ServerConfigGetAsync + ServerConfigAddAsync),
            // seeding the hysteresis fields from appsettings.json so a fresh install has sane
            // defaults before an admin ever visits the settings page.
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
                ScheduleTimeZone = settings.ScheduleTimeZone,
                FirmwareSource = (int)FirmwareSource.GitHub,
                FirmwareGitHubRepository = settings.FirmwareGitHubRepository,
                FirmwareRefreshIntervalHours = settings.FirmwareRefreshIntervalHours,
                SensorDataRetentionDays = settings.SensorDataRetentionDays,
                WeatherPollIntervalMinutes = settings.WeatherPollIntervalMinutes,
                WeatherRainSkipThreshold = settings.WeatherRainSkipThreshold,
                RelayWaitWindowSeconds = 30,
                ProblemEventAlertsEnabled = true,
                ProblemEventExpiryHours = 24,
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
            row.ScheduleTimeZone = config.ScheduleTimeZone;
            row.FirmwareSource = (int)config.FirmwareSource;
            row.FirmwareGitHubRepository = config.FirmwareGitHubRepository;
            row.FirmwareCustomRepositoryUrl = config.FirmwareCustomRepositoryUrl;
            row.FirmwareRefreshIntervalHours = config.FirmwareRefreshIntervalHours;
            // FirmwareLastRefreshedAtUtc deliberately NOT written here - see ServerConfigFirmwareRefreshStateSetAsync below.
            row.SensorDataRetentionDays = config.SensorDataRetentionDays;
            // WeatherRainPredicted/WeatherCheckedAtUtc deliberately NOT written here - they are
            // WeatherEvaluator's computed output (see ServerConfigWeatherStateSetAsync below), so a
            // form post that doesn't know about them can never clobber a fresher reading.
            row.WeatherLocationLat = config.WeatherLocationLat;
            row.WeatherLocationLon = config.WeatherLocationLon;
            row.WeatherPollIntervalMinutes = config.WeatherPollIntervalMinutes;
            row.WeatherRainSkipThreshold = config.WeatherRainSkipThreshold;
            row.RelayEnabled = config.RelayEnabled;
            row.RelayMode = (int)config.RelayMode;
            row.RelayWaitWindowSeconds = config.RelayWaitWindowSeconds;
            row.ProblemEventAlertsEnabled = config.ProblemEventAlertsEnabled;
            row.ProblemEventExpiryHours = config.ProblemEventExpiryHours;
            await db.SaveChangesAsync();

            // Re-applied on every save so an admin edit takes effect immediately on Postgres/
            // TimescaleDB - a no-op on MariaDB/MySQL, whose retention instead comes from
            // SensorDataRetentionBackgroundService reading the row fresh on its next daily tick.
            await ApplyRetentionPolicyAsync(config.SensorDataRetentionDays);
        }

        /// <summary>Forces the DB serverConfig row's hysteresis fields back to appsettings.json's
        /// ServerConfig:Hysteresis values, creating the row if it does not exist yet. Only called
        /// at startup when ServerConfig:Reload is true - see AgrumySettings.ServerConfigReload.</summary>
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
            row.ScheduleTimeZone = settings.ScheduleTimeZone;
            row.SensorDataRetentionDays = settings.SensorDataRetentionDays;
            row.WeatherPollIntervalMinutes = settings.WeatherPollIntervalMinutes;
            row.WeatherRainSkipThreshold = settings.WeatherRainSkipThreshold;
            row.FirmwareRefreshIntervalHours = settings.FirmwareRefreshIntervalHours;
            await db.SaveChangesAsync();
            await ApplyRetentionPolicyAsync(settings.SensorDataRetentionDays);
        }

        /// <summary>The ONLY writer of WeatherRainPredicted/WeatherCheckedAtUtc - called exclusively
        /// by WeatherEvaluator, deliberately narrower than ServerConfigUpdateAsync's full-object
        /// overwrite so the admin Server Settings form can never race a fresher reading back to stale.</summary>
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

        /// <summary>The ONLY writer of FirmwareLastRefreshedAtUtc - called exclusively by
        /// FirmwareCatalogRefreshEvaluator, same isolation reasoning as ServerConfigWeatherStateSetAsync above.</summary>
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

        // Instance, not static: the #94 GitHub-repository fallback below reads the injected settings.
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
            ScheduleTimeZone = r.ScheduleTimeZone,
            FirmwareSource = (FirmwareSource)r.FirmwareSource,
            // A row created before #94 has NULL here; the setting's seed is the right fallback
            // rather than an empty repository nobody can sync from.
            FirmwareGitHubRepository = string.IsNullOrWhiteSpace(r.FirmwareGitHubRepository) ? settings.FirmwareGitHubRepository : r.FirmwareGitHubRepository,
            FirmwareCustomRepositoryUrl = r.FirmwareCustomRepositoryUrl,
            // A row created before #203 has NULL here - same appsettings-seed fallback as
            // FirmwareGitHubRepository above, rather than surfacing "disabled" for every existing install.
            FirmwareRefreshIntervalHours = r.FirmwareRefreshIntervalHours ?? settings.FirmwareRefreshIntervalHours,
            FirmwareLastRefreshedAtUtc = r.FirmwareLastRefreshedAtUtc,
            SensorDataRetentionDays = r.SensorDataRetentionDays,
            WeatherLocationLat = r.WeatherLocationLat,
            WeatherLocationLon = r.WeatherLocationLon,
            // A row created before #11 has NULL here - same appsettings-seed fallback as
            // FirmwareGitHubRepository above, rather than surfacing an empty interval/threshold.
            WeatherPollIntervalMinutes = r.WeatherPollIntervalMinutes ?? settings.WeatherPollIntervalMinutes,
            WeatherRainSkipThreshold = r.WeatherRainSkipThreshold ?? settings.WeatherRainSkipThreshold,
            WeatherRainPredicted = r.WeatherRainPredicted,
            WeatherCheckedAtUtc = r.WeatherCheckedAtUtc,
            RelayEnabled = r.RelayEnabled,
            RelayMode = (RelayMode)r.RelayMode,
            // A row created before this field existed has 0 here, which happens to already equal a sane default
            // (10-300 clamp keeps 0 out of reach otherwise) - not worth a settings.* fallback.
            RelayWaitWindowSeconds = r.RelayWaitWindowSeconds == 0 ? 30 : r.RelayWaitWindowSeconds,
            ProblemEventAlertsEnabled = r.ProblemEventAlertsEnabled,
            // A row created before this field existed has 0 here (column default backfills real rows
            // to 24, but the in-memory generated-default path above always sets it explicitly too).
            ProblemEventExpiryHours = r.ProblemEventExpiryHours == 0 ? 24 : r.ProblemEventExpiryHours,
        };
    }
}
