namespace api.BackgroundWorkers
{
    /// Same deliberate departure as WeatherBackgroundService: fixed 1-minute cadence, not the admin-configured interval - FirmwareCatalogRefreshEvaluator re-checks the live interval every tick.
    public sealed class FirmwareCatalogRefreshBackgroundService(IServiceScopeFactory scopeFactory, ILogger<FirmwareCatalogRefreshBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<FirmwareCatalogRefreshEvaluator>().RunOnceAsync(ct);
    }
}
