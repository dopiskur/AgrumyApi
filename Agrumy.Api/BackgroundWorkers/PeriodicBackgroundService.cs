namespace api.BackgroundWorkers
{
    /// Reusable recurring-work base for hosted services; each tick runs in its own DI scope since IHostedService is singleton-lifetime, and one tick throwing is logged without killing the loop.
    public abstract class PeriodicBackgroundService(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
    {
        protected abstract TimeSpan Interval { get; }

        /// One tick's work, given a fresh DI scope's IServiceProvider; let it throw - ExecuteAsync isolates one tick's failure from the next.
        protected abstract Task DoWorkAsync(IServiceProvider scopedProvider, CancellationToken ct);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            // do{}while, not while{}: runs once immediately on startup rather than waiting a full Interval for the first tick.
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
