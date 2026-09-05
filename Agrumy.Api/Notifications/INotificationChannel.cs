namespace api.Notifications
{
    /// One delivery mechanism for an alert (email, push, ...); <see cref="NotificationDispatcher"/> fans out to every registered channel whose <see cref="IsConfigured"/> is true.
    public interface INotificationChannel
    {
        string Name { get; }

        /// True when this channel has enough config to attempt a send - a disabled/unconfigured channel returns false and is skipped, never treated as a failure.
        bool IsConfigured { get; }

        Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default);
    }
}
