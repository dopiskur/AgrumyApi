using api.Dal.Interface;
using api.Models;
using api.Weather;
using Microsoft.Extensions.Options;

namespace api.BackgroundWorkers
{
    /// <summary>Roadmap #11: computes the single install-wide ServerConfig.WeatherRainPredicted flag
    /// DeviceApiController.BuildDeviceConfigAsync combines with each zone's own opt-in
    /// (DeviceUnitZone.SkipWaterPumpWhenRainPredicted) into the per-device AND-NOT veto. Kept
    /// separate from WeatherBackgroundService so it is directly unit-testable with a mocked
    /// IWeatherForecastClient - same split as LowBatteryAlertEvaluator/BackgroundService.</summary>
    public sealed class WeatherEvaluator(
        IServerConfigRepository serverConfigRepo, IWeatherForecastClient weatherClient, IOptions<AgrumySettings> settingsOptions,
        ILogger<WeatherEvaluator> logger)
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(settings.WeatherApiKey))
            {
                return; // not configured (Weather:ApiKey unset) - inert, same as location below
            }

            ServerConfig config = await serverConfigRepo.ServerConfigGetAsync(1);
            if (config.WeatherLocationLat is not double lat || config.WeatherLocationLon is not double lon)
            {
                return; // roadmap #11: null = admin hasn't set a location yet, same "inert until
                        // configured" rule as ServerConfig.ScheduleTimeZone.
            }

            // Roadmap #11: this check is what actually makes WeatherPollIntervalMinutes live-
            // editable without an app restart - WeatherBackgroundService itself ticks on a FIXED
            // 1-minute cadence (see its own remarks for why), and every one of those ticks re-reads
            // the CURRENT ServerConfig value here, so an admin's edit takes effect on the very next
            // tick instead of waiting for the process to restart.
            int pollMinutes = Math.Max(1, config.WeatherPollIntervalMinutes ?? settings.WeatherPollIntervalMinutes);
            if (config.WeatherCheckedAtUtc is DateTime lastChecked && DateTime.UtcNow - lastChecked < TimeSpan.FromMinutes(pollMinutes))
            {
                return; // not due yet
            }

            double? maxRainPercent = await weatherClient.GetMaxRainProbabilityPercentAsync(lat, lon, settings.WeatherApiKey, ct);
            if (maxRainPercent is not double pop)
            {
                return; // fetch failed (already logged in the client) - leave the last good reading
                        // in place rather than overwrite it with a guess.
            }

            double threshold = config.WeatherRainSkipThreshold ?? settings.WeatherRainSkipThreshold;
            bool rainPredicted = pop >= threshold;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Weather check: max rain probability {Pop}% (threshold {Threshold}%) -> RainPredicted={RainPredicted}.", pop, threshold, rainPredicted);
            }
            await serverConfigRepo.ServerConfigWeatherStateSetAsync(rainPredicted, DateTime.UtcNow, 1);
        }
    }
}
