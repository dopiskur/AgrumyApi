namespace api.BackgroundWorkers
{
    /// Thin PeriodicBackgroundService wrapper - the actual logic lives in DeviceCommandRetentionEvaluator, kept separate for testability.
    public sealed class DeviceCommandRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceCommandRetentionBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<DeviceCommandRetentionEvaluator>().RunOnceAsync(ct);
    }
}
