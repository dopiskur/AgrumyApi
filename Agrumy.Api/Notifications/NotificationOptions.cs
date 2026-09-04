namespace api.Notifications
{
    /// <summary>Bound from the <c>Notifications</c> configuration section.</summary>
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
    }

    public sealed class EmailChannelOptions
    {
        public bool Enabled { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 587;

        /// <summary>587 with STARTTLS (true, default) vs 465 implicit TLS (false).</summary>
        public bool UseStartTls { get; set; } = true;

        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
        public string FromName { get; set; } = "Agrumy";
    }

    /// <summary>FCM (Android/iOS) push. Inert until <see cref="Enabled"/> is set AND the Android app exists to register device tokens - see <see cref="FcmPushNotificationChannel"/>.</summary>
    public sealed class PushChannelOptions
    {
        public bool Enabled { get; set; }

        /// <summary>Firebase project id, for the FCM HTTP v1 endpoint.</summary>
        public string? FcmProjectId { get; set; }

        /// <summary>Path to the Google service-account JSON used to mint FCM access tokens.</summary>
        public string? FcmCredentialsPath { get; set; }
    }

    /// <summary>Generic HTTP POST webhook (Slack incoming-webhook compatible bodies need their own field mapping, this sends Agrumy's own JSON shape) - see <see cref="WebhookNotificationChannel"/>.</summary>
    public sealed class WebhookChannelOptions
    {
        public bool Enabled { get; set; }
        public string? Url { get; set; }

        /// <summary>When set, each request carries an X-Agrumy-Signature header (HMAC-SHA256 of the body) so the receiver can verify it actually came from this Agrumy instance.</summary>
        public string? Secret { get; set; }
    }
}
