using System.Threading.Channels;

namespace api.BackgroundWorkers
{
    /// <summary>On-demand counterpart to PeriodicBackgroundService: a controller action enqueues one job and returns immediately (202 Accepted) instead of blocking on a long-running task. Singleton by necessity (the channel must outlive any one request's DI scope); each job gets its own scope from BackgroundJobRunner.</summary>
    public sealed class BackgroundJobQueue
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> channel =
            Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();

        public void Enqueue(Func<IServiceProvider, CancellationToken, Task> job) =>
            channel.Writer.TryWrite(job);

        public ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader => channel.Reader;
    }

    /// <summary>Runs queued jobs one at a time, in submission order. One job throwing is logged and never stops the runner.</summary>
    public sealed class BackgroundJobRunner(
        BackgroundJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<BackgroundJobRunner> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await job(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // normal shutdown, not a job failure
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background job failed.");
                }
            }
        }
    }
}
