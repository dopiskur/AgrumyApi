namespace api.Notifications
{
    public enum NotificationSeverity
    {
        Info,
        Warning,
        Critical,
    }

    /// <summary>Where a notification goes. A channel uses whichever fields it understands.</summary>
    public sealed record NotificationRecipient(
        string? Email = null,
        IReadOnlyList<string>? PushTokens = null);

    public sealed record Notification(
        string Subject,
        string Body,
        NotificationRecipient Recipient,
        NotificationSeverity Severity = NotificationSeverity.Warning);

    /// <summary>Outcome of one channel handling one notification. <see cref="Sent"/> false covers both
    /// "not applicable / not configured" (<see cref="Skipped"/>) and "tried and failed" (<see cref="Failed"/>).</summary>
    public sealed record NotificationResult(bool Sent, bool Attempted, string? Detail)
    {
        public static NotificationResult Ok(string? detail = null) => new(true, true, detail);
        public static NotificationResult Skipped(string reason) => new(false, false, reason);
        public static NotificationResult Failed(string reason) => new(false, true, reason);
    }
}
