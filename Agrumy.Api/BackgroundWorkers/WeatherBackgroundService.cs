namespace api.BackgroundWorkers
{
    /// <summary>Deliberate departure from the usual PeriodicBackgroundService pattern: Interval is a fixed 1-minute cadence, not sourced from the admin-configured value, because PeriodicBackgroundService reads Interval only once at startup - WeatherEvaluator itself re-checks the live configured interval on every tick, so an admin's edit takes effect without a restart.</summary>
    public sealed class WeatherBackgroundService(IServiceScopeFactory scopeFactory, ILogger<WeatherBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<WeatherEvaluator>().RunOnceAsync(ct);
    }
}
