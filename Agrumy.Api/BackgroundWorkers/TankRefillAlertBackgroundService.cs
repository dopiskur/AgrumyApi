using Microsoft.Extensions.Options;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// Thin PeriodicBackgroundService wrapper - the actual logic lives in TankRefillAlertEvaluator, kept separate for testability.
    public sealed class TankRefillAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<TankRefillAlertBackgroundService> logger)
        : PeriodicBackgroundService(scopeFactory, logger)
    {
        protected override TimeSpan Interval =>
            TimeSpan.FromMinutes(Math.Max(1, options.Value.TankCheckIntervalMinutes));

        protected override Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct) =>
            scopedProvider.GetRequiredService<TankRefillAlertEvaluator>().RunOnceAsync(ct);
    }
}
