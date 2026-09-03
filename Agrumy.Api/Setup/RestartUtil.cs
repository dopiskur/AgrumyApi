namespace api.Setup
{
    /// <summary>Generic self-restart: stops this process cleanly and lets whatever supervises it (systemd's Restart=always, or a container's restart policy) bring it back up. Never calls systemctl/docker directly - the deploy accounts this runs under have no sudo.</summary>
    internal static class RestartUtil
    {
        /// <summary>Fire-and-forget on purpose: the caller (a POST handler) still needs to finish writing its HTTP response before the process exits, so the stop is scheduled a moment later rather than called inline - StopApplication() begins shutdown immediately, which would otherwise race the response still being flushed.</summary>
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
