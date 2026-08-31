using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Server-wide settings facet (roadmap #74).</summary>
    public interface IServerConfigRepository
    {
        Task<ServerConfig> ServerConfigGetAsync(int idServerConfig);
        Task ServerConfigUpdateAsync(ServerConfig config);

        /// <summary>Overwrites the DB row's hysteresis fields from appsettings.json (roadmap #10) -
        /// only called at startup when Config.serverConfigReload is true.</summary>
        Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig);
    }
}
