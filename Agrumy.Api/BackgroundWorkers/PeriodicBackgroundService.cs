namespace api.BackgroundWorkers
{
    /// <summary>Reusable recurring-work base for hosted services. Each tick runs in its own DI scope, since IHostedService is singleton-lifetime and every repository/dispatcher here is scoped; one tick throwing is logged and never kills the loop.</summary>
    public abstract class PeriodicBackgroundService(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
    {
        protected abstract TimeSpan Interval { get; }

        /// <summary>One tick's work, given a fresh DI scope's IServiceProvider. Let it throw - ExecuteAsync isolates one tick's failure from the next.</summary>
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
