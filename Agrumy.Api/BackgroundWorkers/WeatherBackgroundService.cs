namespace api.BackgroundWorkers
{
    /// <summary>Roadmap #11 (feature) + #40 (pattern), with a deliberate departure from the pattern:
    /// unlike every other PeriodicBackgroundService below, Interval here is NOT sourced from the
    /// admin-configured value (ServerConfig.WeatherPollIntervalMinutes). PeriodicBackgroundService
    /// reads Interval exactly once, at PeriodicTimer construction before the loop starts - a value
    /// read from the DB there would freeze at whatever it was on startup and never notice a later
    /// admin edit without an app restart, which the user explicitly required NOT be the case ("mora
    /// biti konfigurabilno"). Instead this ticks on a fixed, cheap 1-minute cadence, and
    /// WeatherEvaluator itself decides on EVERY tick whether the configured interval has actually
    /// elapsed (comparing the live ServerConfig.WeatherCheckedAtUtc against the live
    /// WeatherPollIntervalMinutes) before it ever calls OpenWeatherMap - so an admin's edit takes
    /// effect on the very next tick, not after a restart.</summary>
    public sealed class WeatherBackgroundService(IServiceScopeFactory scopeFactory, ILogger<WeatherBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<WeatherEvaluator>().RunOnceAsync(ct);
    }
}
