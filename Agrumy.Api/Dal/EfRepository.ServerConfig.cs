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
            await using var db = Db();
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
                WaterLevelHysteresis = Config.hysteresisWaterLevel,
                TemperatureHysteresis = Config.hysteresisTemperature,
                HumidityHysteresis = Config.hysteresisHumidity,
                LightHysteresis = Config.hysteresisLight,
                EventDedupeMinutes = Config.eventDedupeMinutes,
                ActivationResendCooldownMinutes = Config.activationResendCooldownMinutes,
                AllowSelfServiceTenantCreation = Config.allowSelfServiceTenantCreation,
            };
            db.ServerConfigs.Add(generated);
            await db.SaveChangesAsync();
            return ToDto(generated);
        }

        public async Task ServerConfigUpdateAsync(ServerConfig config)
        {
            await using var db = Db();
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
            await db.SaveChangesAsync();
        }

        /// <summary>Forces the DB serverConfig row's hysteresis fields back to appsettings.json's
        /// ServerConfig:Hysteresis values, creating the row if it does not exist yet. Only called
        /// at startup when ServerConfig:Reload is true - see Config.serverConfigReload.</summary>
        public async Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig = 1)
        {
            await using var db = Db();
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

            row.WaterLevelHysteresis = Config.hysteresisWaterLevel;
            row.TemperatureHysteresis = Config.hysteresisTemperature;
            row.HumidityHysteresis = Config.hysteresisHumidity;
            row.LightHysteresis = Config.hysteresisLight;
            row.EventDedupeMinutes = Config.eventDedupeMinutes;
            row.ActivationResendCooldownMinutes = Config.activationResendCooldownMinutes;
            row.AllowSelfServiceTenantCreation = Config.allowSelfServiceTenantCreation;
            await db.SaveChangesAsync();
        }

        private static ServerConfig ToDto(ServerConfigRow r) => new()
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
        };
    }
}
