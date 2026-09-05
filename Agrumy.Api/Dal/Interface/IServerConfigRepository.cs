using api.Models;

namespace api.Dal.Interface
{
    /// Server-wide settings facet.
    public interface IServerConfigRepository
    {
        Task<ServerConfig> ServerConfigGetAsync(int idServerConfig);
        Task ServerConfigUpdateAsync(ServerConfig config);

        /// Overwrites the DB row's hysteresis fields from appsettings.json - only called at startup when AgrumySettings.ServerConfigReload is true.
        Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig);

        /// Narrow writer for WeatherEvaluator's computed result, kept separate from ServerConfigUpdateAsync so a concurrent settings save can't race it.
        Task ServerConfigWeatherStateSetAsync(bool rainPredicted, DateTime checkedAtUtc, int idServerConfig);

        /// Narrow writer for FirmwareCatalogRefreshEvaluator's last-run timestamp, same isolation reasoning as ServerConfigWeatherStateSetAsync.
        Task ServerConfigFirmwareRefreshStateSetAsync(DateTime checkedAtUtc, int idServerConfig);
    }
}
