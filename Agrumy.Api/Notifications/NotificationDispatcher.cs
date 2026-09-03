namespace api.Notifications
{
    /// <summary>Fans one notification out to every configured channel.</summary>
    public interface INotificationDispatcher
    {
        Task<IReadOnlyList<ChannelOutcome>> DispatchAsync(Notification notification, CancellationToken ct = default);
    }

    public sealed record ChannelOutcome(string Channel, NotificationResult Result);

    public sealed class NotificationDispatcher : INotificationDispatcher
    {
        private readonly IEnumerable<INotificationChannel> _channels;
        private readonly ILogger<NotificationDispatcher> _logger;

        public NotificationDispatcher(IEnumerable<INotificationChannel> channels, ILogger<NotificationDispatcher> logger)
        {
            _channels = channels;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ChannelOutcome>> DispatchAsync(Notification notification, CancellationToken ct = default)
        {
            var outcomes = new List<ChannelOutcome>();

            foreach (var channel in _channels)
            {
                if (!channel.IsConfigured)
                {
                    outcomes.Add(new ChannelOutcome(channel.Name, NotificationResult.Skipped("channel not configured")));
                    continue;
                }

                NotificationResult result;
                try
                {
                    result = await channel.SendAsync(notification, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification channel {Channel} threw.", channel.Name);
                    result = NotificationResult.Failed(ex.Message);
                }
                outcomes.Add(new ChannelOutcome(channel.Name, result));
            }

            if (outcomes.All(o => !o.Result.Sent))
            {
                _logger.LogWarning("Notification \"{Subject}\" was not delivered by any channel.", notification.Subject);
            }
            return outcomes;
        }
    }
}
