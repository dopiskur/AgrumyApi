using api;
using api.BackgroundWorkers;
using api.Dal.Interface;
using api.Models;
using api.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #11 (feature) + #40 (pattern). Exercises WeatherEvaluator directly - no
/// PeriodicTimer/IHostedService in the way, no database or real HTTP call (repository/forecast
/// client are mocked). Strict mocks double as the assertion that an early-return path makes NO
/// further calls (unconfigured, not due yet, fetch failed).
/// </summary>
public class WeatherEvaluatorTests
{
    private readonly Mock<IServerConfigRepository> _serverConfig = new(MockBehavior.Strict);
    private readonly Mock<IWeatherForecastClient> _weatherClient = new(MockBehavior.Strict);

    private WeatherEvaluator NewEvaluator(string? apiKey = "test-key") =>
        new(_serverConfig.Object, _weatherClient.Object,
            Options.Create(new AgrumySettings { WeatherApiKey = apiKey, WeatherPollIntervalMinutes = 15, WeatherRainSkipThreshold = 50.0 }),
            NullLogger<WeatherEvaluator>.Instance);

    private void SetupServerConfig(ServerConfig config) =>
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1)).ReturnsAsync(config);

    [Fact]
    public async Task No_ApiKey_Configured_Does_Nothing()
    {
        await NewEvaluator(apiKey: null).RunOnceAsync();
        // Strict mocks: ServerConfigGetAsync/GetMaxRainProbabilityPercentAsync would throw if called.
    }

    [Theory]
    [InlineData(null, 15.0)]
    [InlineData(45.5, null)]
    public async Task Location_Not_Fully_Set_Does_Nothing(double? lat, double? lon)
    {
        SetupServerConfig(new ServerConfig { WeatherLocationLat = lat, WeatherLocationLon = lon });

        await NewEvaluator().RunOnceAsync();

        // Strict mock: GetMaxRainProbabilityPercentAsync would throw if called.
    }

    [Fact]
    public async Task Not_Due_Yet_Skips_The_Fetch()
    {
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = 15,
            WeatherCheckedAtUtc = DateTime.UtcNow.AddMinutes(-5), // < 15 minutes ago
        });

        await NewEvaluator().RunOnceAsync();

        // Strict mock: GetMaxRainProbabilityPercentAsync would throw if called.
    }

    [Fact]
    public async Task Never_Checked_Before_Is_Due_Immediately()
    {
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = 15,
            WeatherCheckedAtUtc = null,
            WeatherRainSkipThreshold = 50.0,
        });
        _weatherClient.Setup(c => c.GetMaxRainProbabilityPercentAsync(45.8, 16.0, "test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10.0);
        _serverConfig.Setup(s => s.ServerConfigWeatherStateSetAsync(false, It.IsAny<DateTime>(), 1)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _serverConfig.Verify(s => s.ServerConfigWeatherStateSetAsync(false, It.IsAny<DateTime>(), 1), Times.Once);
    }

    [Fact]
    public async Task Rain_Probability_At_Or_Above_Threshold_Sets_RainPredicted_True()
    {
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = 15,
            WeatherCheckedAtUtc = DateTime.UtcNow.AddHours(-1),
            WeatherRainSkipThreshold = 50.0,
        });
        _weatherClient.Setup(c => c.GetMaxRainProbabilityPercentAsync(45.8, 16.0, "test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(50.0); // exactly at threshold - counts as predicted, same >= convention as LowBatteryAlertEvaluator's threshold check
        _serverConfig.Setup(s => s.ServerConfigWeatherStateSetAsync(true, It.IsAny<DateTime>(), 1)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _serverConfig.Verify(s => s.ServerConfigWeatherStateSetAsync(true, It.IsAny<DateTime>(), 1), Times.Once);
    }

    [Fact]
    public async Task Rain_Probability_Below_Threshold_Sets_RainPredicted_False()
    {
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = 15,
            WeatherCheckedAtUtc = DateTime.UtcNow.AddHours(-1),
            WeatherRainSkipThreshold = 50.0,
        });
        _weatherClient.Setup(c => c.GetMaxRainProbabilityPercentAsync(45.8, 16.0, "test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(49.9);
        _serverConfig.Setup(s => s.ServerConfigWeatherStateSetAsync(false, It.IsAny<DateTime>(), 1)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _serverConfig.Verify(s => s.ServerConfigWeatherStateSetAsync(false, It.IsAny<DateTime>(), 1), Times.Once);
    }

    [Fact]
    public async Task Failed_Fetch_Leaves_Last_Known_State_Untouched()
    {
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = 15,
            WeatherCheckedAtUtc = DateTime.UtcNow.AddHours(-1),
        });
        _weatherClient.Setup(c => c.GetMaxRainProbabilityPercentAsync(45.8, 16.0, "test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);

        await NewEvaluator().RunOnceAsync();

        // Strict mock: ServerConfigWeatherStateSetAsync would throw if called - a failed fetch must
        // not overwrite the last good reading with a guess.
    }

    [Fact]
    public async Task Missing_ServerConfig_PollInterval_Falls_Back_To_AgrumySettings_Default()
    {
        // Roadmap #11: a row created before this feature has WeatherPollIntervalMinutes == null -
        // must fall back to AgrumySettings.WeatherPollIntervalMinutes (15 here), not treat null as
        // "always due" or throw.
        SetupServerConfig(new ServerConfig
        {
            WeatherLocationLat = 45.8,
            WeatherLocationLon = 16.0,
            WeatherPollIntervalMinutes = null,
            WeatherCheckedAtUtc = DateTime.UtcNow.AddMinutes(-5), // < 15 minutes ago -> not due
        });

        await NewEvaluator().RunOnceAsync();

        // Strict mock: GetMaxRainProbabilityPercentAsync would throw if called.
    }
}
