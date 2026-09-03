namespace api.BackgroundWorkers
{
    /// <summary>Thin PeriodicBackgroundService wrapper - all the actual logic lives in
    /// SensorDataRetentionEvaluator (kept separate so it's testable without a running timer), same
    /// shape as OfflineAlertBackgroundService/LowBatteryAlertBackgroundService.</summary>
    public sealed class SensorDataRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SensorDataRetentionBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<SensorDataRetentionEvaluator>().RunOnceAsync(ct);
    }
}
