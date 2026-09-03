using Microsoft.Extensions.Options;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>Thin PeriodicBackgroundService wrapper - the actual logic lives in OfflineAlertEvaluator, kept separate for testability.</summary>
    public sealed class OfflineAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<OfflineAlertBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval =>
            TimeSpan.FromMinutes(Math.Max(1, options.Value.OfflineCheckIntervalMinutes));

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<OfflineAlertEvaluator>().RunOnceAsync(ct);
    }
}
