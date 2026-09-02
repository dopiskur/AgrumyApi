using Microsoft.Extensions.Options;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>Thin PeriodicBackgroundService wrapper - all the actual logic lives in
    /// LowBatteryAlertEvaluator (kept separate so it's testable without a running timer). Roadmap
    /// #12, same shape as OfflineAlertBackgroundService.</summary>
    public sealed class LowBatteryAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<LowBatteryAlertBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval =>
            TimeSpan.FromMinutes(Math.Max(1, options.Value.BatteryCheckIntervalMinutes));

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<LowBatteryAlertEvaluator>().RunOnceAsync(ct);
    }
}
