using api.Dal.Interface;
using api.Firmware;
using api.Models;
using Microsoft.Extensions.Options;

namespace api.BackgroundWorkers
{
    /// Periodically re-syncs the firmware catalog from its active source, same fixed-tick/live-reread-interval pattern as WeatherEvaluator.
    public sealed class FirmwareCatalogRefreshEvaluator(
        IServerConfigRepository serverConfigRepo, FirmwareCatalogService catalog, IOptions<AgrumySettings> settingsOptions,
        ILogger<FirmwareCatalogRefreshEvaluator> logger)
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            ServerConfig config = await serverConfigRepo.ServerConfigGetAsync(1);
            if (config.FirmwareRefreshIntervalHours is not int intervalHours || intervalHours <= 0)
            {
                return; // disabled
            }

            // FirmwareCatalogRefreshBackgroundService ticks every minute and re-reads this live value, so an admin's edit takes effect without a restart.
            if (config.FirmwareLastRefreshedAtUtc is DateTime lastRefreshed && DateTime.UtcNow - lastRefreshed < TimeSpan.FromHours(intervalHours))
            {
                return; // not due yet
            }

            // Local-source Refresh falls through to a GitHub->local pull (see SyncAsync), which needs a real public URL to build download links from - a background tick has no HTTP request to derive one from.
            if (config.FirmwareSource == FirmwareSource.Local && string.IsNullOrWhiteSpace(settings.ApiService))
            {
                return;
            }

            FirmwareSyncResult result = await catalog.SyncAsync(FirmwareSyncMode.Refresh, settings.ApiService ?? "", ct);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Firmware catalog auto-refresh: added {Added}, removed {Removed}, skipped {Skipped}.", result.Added, result.Removed, result.Skipped);
            }
            await serverConfigRepo.ServerConfigFirmwareRefreshStateSetAsync(DateTime.UtcNow, 1);
        }
    }
}
