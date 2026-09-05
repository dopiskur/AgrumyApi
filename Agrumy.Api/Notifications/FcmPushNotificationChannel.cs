using System.Text.Json;
using Microsoft.Extensions.Options;

namespace api.Notifications
{
    /// Firebase Cloud Messaging push channel (Android now, iOS via APNs later) - PREPARED, NOT LIVE: stays skipped since <see cref="PushChannelOptions.Enabled"/> defaults false and no Android app exists yet to supply <see cref="NotificationRecipient.PushTokens"/>.
    public sealed class FcmPushNotificationChannel : INotificationChannel
    {
        private const string FcmSendEndpoint = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";

        private readonly PushChannelOptions _options;
        private readonly ILogger<FcmPushNotificationChannel> _logger;

        public FcmPushNotificationChannel(IOptions<NotificationOptions> options, ILogger<FcmPushNotificationChannel> logger)
        {
            _options = options.Value.Push;
            _logger = logger;
        }

        public string Name => "push-fcm";

        public bool IsConfigured =>
            _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.FcmProjectId)
            && !string.IsNullOrWhiteSpace(_options.FcmCredentialsPath)
            && File.Exists(_options.FcmCredentialsPath);

        public Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return Task.FromResult(NotificationResult.Skipped(
                    "push channel not active - waiting on the Android app + FCM credentials (see class remarks)"));
            }

            var tokens = notification.Recipient.PushTokens;
            if (tokens is null || tokens.Count == 0)
            {
                return Task.FromResult(NotificationResult.Skipped("recipient has no registered device tokens"));
            }

            // Config is present but the OAuth token step is deliberately not wired - fail loudly rather than silently no-op.
            _logger.LogError(
                "FCM push is configured but not wired: GetAccessTokenAsync needs an OAuth2 token provider. " +
                "See FcmPushNotificationChannel remarks.");
            return Task.FromResult(NotificationResult.Failed("FCM send not implemented - missing OAuth2 token provider"));
        }

        /// FCM HTTP v1 message body for a single device token. Ready for use once <c>GetAccessTokenAsync</c> exists.
        internal static string BuildFcmPayload(Notification notification, string deviceToken)
        {
            var message = new
            {
                message = new
                {
                    token = deviceToken,
                    notification = new
                    {
                        title = notification.Subject,
                        body = notification.Body,
                    },
                    data = new Dictionary<string, string>
                    {
                        ["severity"] = notification.Severity.ToString(),
                    },
                },
            };
            return JsonSerializer.Serialize(message);
        }

        internal static string SendEndpointFor(string projectId) => string.Format(FcmSendEndpoint, projectId);
    }
}
