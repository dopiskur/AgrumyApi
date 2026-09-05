using Microsoft.Extensions.Options;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// Thin PeriodicBackgroundService wrapper - the actual logic lives in RuleNotificationEvaluator, kept separate for testability.
    public sealed class RuleNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<RuleNotificationBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval =>
            TimeSpan.FromMinutes(Math.Max(1, options.Value.RuleCheckIntervalMinutes));

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<RuleNotificationEvaluator>().RunOnceAsync(ct);
    }
}
