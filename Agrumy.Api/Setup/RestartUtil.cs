namespace api.Setup
{
    /// Generic self-restart: stops this process cleanly and lets whatever supervises it bring it back up; never calls systemctl/docker directly since deploy accounts have no sudo.
    internal static class RestartUtil
    {
        /// Fire-and-forget on purpose - the caller's HTTP response must finish flushing before the process exits, so StopApplication() is scheduled a moment later rather than called inline.
        public static void ScheduleRestart(IHostApplicationLifetime lifetime, ILogger logger, string reason)
        {
            logger.LogWarning("Scheduling restart to apply: {Reason}", reason);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                lifetime.StopApplication();
            });
        }
    }
}
