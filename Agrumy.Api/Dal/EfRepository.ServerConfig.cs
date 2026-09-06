using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IServerConfigRepository members - forwarded to the standalone EfServerConfigRepository (roadmap #246) so IRepository's broad consumers keep working unchanged. Defaults preserved on idServerConfig since some internal callers (Devices.cs, DeviceFarmUnits.cs, Devices.Diagnostics.cs) rely on them.
    internal partial class EfRepository
    {
        public Task<ServerConfig> ServerConfigGetAsync(int idServerConfig = 1) => serverConfigRepository.ServerConfigGetAsync(idServerConfig);

        public Task ServerConfigUpdateAsync(ServerConfig config) => serverConfigRepository.ServerConfigUpdateAsync(config);

        public Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig = 1) => serverConfigRepository.ServerConfigReloadFromAppSettingsAsync(idServerConfig);

        public Task ServerConfigWeatherStateSetAsync(bool rainPredicted, DateTime checkedAtUtc, int idServerConfig = 1) =>
            serverConfigRepository.ServerConfigWeatherStateSetAsync(rainPredicted, checkedAtUtc, idServerConfig);

        public Task ServerConfigFirmwareRefreshStateSetAsync(DateTime checkedAtUtc, int idServerConfig = 1) =>
            serverConfigRepository.ServerConfigFirmwareRefreshStateSetAsync(checkedAtUtc, idServerConfig);

        public Task ApplyRetentionPolicyAsync(int? retentionDays) => serverConfigRepository.ApplyRetentionPolicyAsync(retentionDays);
    }
}
