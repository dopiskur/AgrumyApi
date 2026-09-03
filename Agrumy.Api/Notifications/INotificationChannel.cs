namespace api.Notifications
{
    /// <summary>One delivery mechanism for an alert (email, push, ...). Registered as <c>IEnumerable&lt;INotificationChannel&gt;</c>; <see cref="NotificationDispatcher"/> fans out to every channel whose <see cref="IsConfigured"/> is true.</summary>
    public interface INotificationChannel
    {
        string Name { get; }

        /// <summary>True when this channel has enough config to attempt a send. A disabled or
        /// unconfigured channel returns false and is skipped, never treated as a failure.</summary>
        bool IsConfigured { get; }

        Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default);
    }
}
