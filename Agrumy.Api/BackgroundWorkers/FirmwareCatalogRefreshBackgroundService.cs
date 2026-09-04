namespace api.BackgroundWorkers
{
    /// <summary>Same deliberate departure as WeatherBackgroundService: fixed 1-minute cadence, not the admin-configured interval - FirmwareCatalogRefreshEvaluator itself re-checks the live interval every tick.</summary>
    public sealed class FirmwareCatalogRefreshBackgroundService(IServiceScopeFactory scopeFactory, ILogger<FirmwareCatalogRefreshBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<FirmwareCatalogRefreshEvaluator>().RunOnceAsync(ct);
    }
}
