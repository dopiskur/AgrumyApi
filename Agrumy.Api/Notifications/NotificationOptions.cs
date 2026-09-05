namespace api.Notifications
{
    /// Bound from the <c>Notifications</c> configuration section.
    public sealed class NotificationOptions
    {
        public const string SectionName = "Notifications";

        public EmailChannelOptions Email { get; set; } = new();
        public PushChannelOptions Push { get; set; } = new();
        public WebhookChannelOptions Webhook { get; set; } = new();

        // How often OfflineAlertBackgroundService sweeps every device; 5 minutes comfortably beats ComputeOnline's minimum ~90s grace window without hammering the DB on every tick.
        public int OfflineCheckIntervalMinutes { get; set; } = 5;

        // How often LowBatteryAlertEvaluator sweeps battery readings; longer than OfflineCheckIntervalMinutes by default since a battery drains over hours/days, not seconds.
        public int BatteryCheckIntervalMinutes { get; set; } = 30;

        // How often RuleNotificationEvaluator (#212) sweeps Notification-action rules; same cadence as
        // OfflineCheckIntervalMinutes - a rule-driven alert is meant to feel timely, not battery-drain-slow.
        public int RuleCheckIntervalMinutes { get; set; } = 5;
    }

    public sealed class EmailChannelOptions
    {
        public bool Enabled { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 587;

        /// 587 with STARTTLS (true, default) vs 465 implicit TLS (false).
        public bool UseStartTls { get; set; } = true;

        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string FromName { get; set; } = "Agrumy";
    }

    /// FCM (Android/iOS) push - inert until <see cref="Enabled"/> is set AND the Android app exists to register device tokens; see <see cref="FcmPushNotificationChannel"/>.
    public sealed class PushChannelOptions
    {
        public bool Enabled { get; set; }

        /// Firebase project id, for the FCM HTTP v1 endpoint.
        public string? FcmProjectId { get; set; }

        /// Path to the Google service-account JSON used to mint FCM access tokens.
        public string? FcmCredentialsPath { get; set; }
    }

    /// Generic HTTP POST webhook sending Agrumy's own JSON shape (not Slack-compatible) - see <see cref="WebhookNotificationChannel"/>.
    public sealed class WebhookChannelOptions
    {
        public bool Enabled { get; set; }
        public string? Url { get; set; }

        /// When set, each request carries an X-Agrumy-Signature header (HMAC-SHA256 of the body) so the receiver can verify it actually came from this Agrumy instance.
        public string? Secret { get; set; }
    }
}
