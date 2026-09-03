namespace api.Setup
{
    /// <summary>Roadmap #30/#139: "build once, use twice" generic self-restart - the DB-setup
    /// wizard below (after writing a new appsettings.json) and the future #139 auto-update (after
    /// swapping the binary on disk) both need the exact same thing: stop this process cleanly and
    /// let whatever supervises it bring it back up. That's systemd's Restart=always for the
    /// bare-metal path (kestrel-agrumy.service.template) or a container's restart policy for the
    /// Docker/Podman path - never a systemctl/docker call from inside this process, which
    /// wouldn't work anyway since the deploy accounts this runs under have no sudo (CLAUDE.md).
    /// A container never reaches this at all in practice - the wizard only triggers when
    /// ConnectionStrings:DefaultConnection is missing, and a container's appsettings.json always
    /// arrives already populated (roadmap #30's container-vs-bare-metal split) - but the same
    /// restart mechanism works there unchanged if #139 ever needs it for that path too.</summary>
    internal static class RestartUtil
    {
        /// <summary>Fire-and-forget on purpose: the caller (a POST handler) still needs to finish
        /// writing its HTTP response before the process exits, so the actual stop is scheduled a
        /// moment later rather than called inline - StopApplication() begins a graceful shutdown
        /// immediately, which would otherwise race the response still being flushed to the client.</summary>
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
