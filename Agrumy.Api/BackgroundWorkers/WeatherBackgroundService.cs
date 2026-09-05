namespace api.BackgroundWorkers
{
    /// Fixed 1-minute cadence, not the admin-configured interval - PeriodicBackgroundService reads Interval only once at startup, so WeatherEvaluator re-checks the live value every tick instead.
    public sealed class WeatherBackgroundService(IServiceScopeFactory scopeFactory, ILogger<WeatherBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<WeatherEvaluator>().RunOnceAsync(ct);
    }
}
