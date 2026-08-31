namespace api.BackgroundWorkers
{
    /// <summary>Roadmap #40: reusable recurring-work base for hosted services - OfflineAlertBackgroundService
    /// is the first consumer, future work (#13 report generation, further telemetry aggregation) is
    /// meant to derive from this rather than hand-roll its own BackgroundService. Each tick runs in
    /// its own DI scope, since IHostedService itself is singleton-lifetime and every repository/
    /// dispatcher in this codebase is scoped; one tick throwing is logged and never kills the loop,
    /// the same way one bad HTTP request doesn't take down the next one.</summary>
    public abstract class PeriodicBackgroundService(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
    {
        protected abstract TimeSpan Interval { get; }

        /// <summary>One tick's work, given a fresh DI scope's IServiceProvider. Let it throw -
        /// ExecuteAsync below isolates one tick's failure from the next.</summary>
        protected abstract Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            // Runs once immediately on startup (do{}while, not while{}) rather than waiting a full
            // Interval for the first tick - an offline device already down at boot should not wait
            // up to Interval minutes for its first alert.
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await DoWorkAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // normal shutdown, not a tick failure
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "{Worker} tick failed - will retry next interval.", GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
