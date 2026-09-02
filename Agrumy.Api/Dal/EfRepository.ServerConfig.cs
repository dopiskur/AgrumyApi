using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IServerConfigRepository members (roadmap #74 split).</summary>
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
                EventDedupeMinutes = settings.EventDedupeMinutes,
                ActivationResendCooldownMinutes = settings.ActivationResendCooldownMinutes,
                AllowSelfServiceTenantCreation = settings.AllowSelfServiceTenantCreation,
                ScheduleTimeZone = settings.ScheduleTimeZone,
                FirmwareSource = (int)FirmwareSource.GitHub,
                FirmwareGitHubRepository = settings.FirmwareGitHubRepository,
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
            row.EventDedupeMinutes = config.EventDedupeMinutes;
            row.ActivationResendCooldownMinutes = config.ActivationResendCooldownMinutes;
            row.AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation;
            row.ScheduleTimeZone = config.ScheduleTimeZone;
            row.FirmwareSource = (int)config.FirmwareSource;
            row.FirmwareGitHubRepository = config.FirmwareGitHubRepository;
            row.FirmwareCustomRepositoryUrl = config.FirmwareCustomRepositoryUrl;
            await db.SaveChangesAsync();
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
            row.EventDedupeMinutes = settings.EventDedupeMinutes;
            row.ActivationResendCooldownMinutes = settings.ActivationResendCooldownMinutes;
            row.AllowSelfServiceTenantCreation = settings.AllowSelfServiceTenantCreation;
            row.ScheduleTimeZone = settings.ScheduleTimeZone;
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
            EventDedupeMinutes = r.EventDedupeMinutes,
            ActivationResendCooldownMinutes = r.ActivationResendCooldownMinutes,
            AllowSelfServiceTenantCreation = r.AllowSelfServiceTenantCreation,
            ScheduleTimeZone = r.ScheduleTimeZone,
            FirmwareSource = (FirmwareSource)r.FirmwareSource,
            // A row created before #94 has NULL here; the setting's seed is the right fallback
            // rather than an empty repository nobody can sync from.
            FirmwareGitHubRepository = string.IsNullOrWhiteSpace(r.FirmwareGitHubRepository) ? settings.FirmwareGitHubRepository : r.FirmwareGitHubRepository,
            FirmwareCustomRepositoryUrl = r.FirmwareCustomRepositoryUrl,
        };
    }
}
