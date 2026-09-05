namespace api.Notifications
{
    public enum NotificationSeverity
    {
        Info,
        Warning,
        Critical,
    }

    /// Where a notification goes. A channel uses whichever fields it understands.
    public sealed record NotificationRecipient(
        string? Email = null,
        IReadOnlyList<string>? PushTokens = null);

    /// ContainsSecret marks a notification whose Body carries a live credential (e.g. an activation-link token) - WebhookNotificationChannel refuses to forward those to its shared, operator-configured endpoint.
    public sealed record Notification(
        string Subject,
        string Body,
        NotificationRecipient Recipient,
        NotificationSeverity Severity = NotificationSeverity.Warning,
        bool ContainsSecret = false);

    /// Outcome of one channel handling one notification; <see cref="Sent"/> false covers both "not applicable" (<see cref="Skipped"/>) and "tried and failed" (<see cref="Failed"/>).
    public sealed record NotificationResult(bool Sent, bool Attempted, string? Detail)
    {
        public static NotificationResult Ok(string? detail = null) => new(true, true, detail);
        public static NotificationResult Skipped(string reason) => new(false, false, reason);
        public static NotificationResult Failed(string reason) => new(false, true, reason);
    }
}
