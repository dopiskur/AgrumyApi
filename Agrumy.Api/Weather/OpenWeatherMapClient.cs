using System.Globalization;
using System.Text.Json;

namespace api.Weather
{
    /// Abstraction over the actual HTTP call, so WeatherEvaluator's own logic is unit-testable with a mock - same shape as api.Firmware.IFirmwareFetcher.
    public interface IWeatherForecastClient
    {
        /// Null means the request failed (already logged) - the caller must leave the last known state alone rather than treat a failed fetch as "no rain".
        Task<double?> GetMaxRainProbabilityPercentAsync(double lat, double lon, string apiKey, CancellationToken ct);
    }

    /// Uses OpenWeatherMap's free "5 day / 3 hour forecast" endpoint, not the paid One Call 3.0 - each bucket's "pop" (0-1) is a closer match to "will it rain soon" than the current-conditions endpoint.
    public sealed class OpenWeatherMapClient(HttpClient httpClient, ILogger<OpenWeatherMapClient> logger) : IWeatherForecastClient
    {
        private const string ForecastUrl = "https://api.openweathermap.org/data/2.5/forecast";

        // 8 buckets x 3h = next 24h - long enough to catch an approaching front, short enough that a "skip today's watering" decision still means something by the time it's acted on.
        private const int LookaheadBuckets = 8;

        public async Task<double?> GetMaxRainProbabilityPercentAsync(double lat, double lon, string apiKey, CancellationToken ct)
        {
            string url = $"{ForecastUrl}?lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lon.ToString(CultureInfo.InvariantCulture)}&appid={Uri.EscapeDataString(apiKey)}";
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("OpenWeatherMap forecast request failed with {Status}.", response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("list", out JsonElement list))
                {
                    return null;
                }

                double maxPop = 0;
                int seen = 0;
                foreach (JsonElement entry in list.EnumerateArray())
                {
                    if (seen++ >= LookaheadBuckets)
                    {
                        break;
                    }
                    if (entry.TryGetProperty("pop", out JsonElement popElement) && popElement.TryGetDouble(out double pop))
                    {
                        maxPop = Math.Max(maxPop, pop);
                    }
                }
                return maxPop * 100.0;
            }
            // Deliberately NOT catching OperationCanceledException/TaskCanceledException - a shutdown-triggered cancellation should propagate to PeriodicBackgroundService's own handler, not be logged as a fetch failure.
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                logger.LogWarning(ex, "OpenWeatherMap forecast fetch failed.");
                return null;
            }
        }
    }
}
