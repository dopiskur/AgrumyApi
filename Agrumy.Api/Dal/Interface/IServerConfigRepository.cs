using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Server-wide settings facet.</summary>
    public interface IServerConfigRepository
    {
        Task<ServerConfig> ServerConfigGetAsync(int idServerConfig);
        Task ServerConfigUpdateAsync(ServerConfig config);

        /// <summary>Overwrites the DB row's hysteresis fields from appsettings.json - only called at
        /// startup when AgrumySettings.ServerConfigReload is true.</summary>
        Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig);

        /// <summary>Narrow writer for WeatherEvaluator's computed result - see the EfRepository
        /// implementation's remarks for why this is separate from ServerConfigUpdateAsync.</summary>
        Task ServerConfigWeatherStateSetAsync(bool rainPredicted, DateTime checkedAtUtc, int idServerConfig);
    }
}
