namespace api.BackgroundWorkers
{
    /// <summary>Thin PeriodicBackgroundService wrapper - the actual logic lives in SensorDataRetentionEvaluator, kept separate for testability.</summary>
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
