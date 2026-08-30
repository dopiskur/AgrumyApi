using System.Text.Json;
using Microsoft.Extensions.Options;

namespace api.Notifications
{
    /// <summary>
    /// Firebase Cloud Messaging push channel (Android now, iOS via APns later). PREPARED, NOT LIVE.
    ///
    /// It stays skipped because <see cref="PushChannelOptions.Enabled"/> defaults to false and,
    /// more fundamentally, nothing yet supplies <see cref="NotificationRecipient.PushTokens"/> -
    /// device tokens come from the Android app registering with the API, which does not exist yet
    /// (roadmap #22/#27).
    ///
    /// To activate once the app ships:
    ///  1. Add a package that mints Google OAuth2 access tokens from a service account
    ///     (Google.Apis.Auth, or FirebaseAdmin which also wraps the send call).
    ///  2. In <see cref="SendAsync"/>, get an access token from <see cref="PushChannelOptions.FcmCredentialsPath"/>
    ///     and POST <see cref="BuildFcmPayload"/> to <see cref="SendEndpointFor"/> per device token.
    ///  3. Add an endpoint + storage for per-user device tokens and populate
    ///     <see cref="NotificationRecipient.PushTokens"/> from it.
    ///  4. Set <c>Notifications:Push:Enabled=true</c> and <c>FcmProjectId</c>.
    /// The FCM HTTP v1 request shape and endpoint are already built below, so the OAuth token is the
    /// only real work left on this class.
    /// </summary>
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

            // Config is present but the OAuth token step is deliberately not wired - fail loudly
            // rather than silently no-op, so flipping Enabled on prematurely is obvious.
            _logger.LogError(
                "FCM push is configured but not wired: GetAccessTokenAsync needs an OAuth2 token provider. " +
                "See FcmPushNotificationChannel remarks.");
            return Task.FromResult(NotificationResult.Failed("FCM send not implemented - missing OAuth2 token provider"));
        }

        /// <summary>FCM HTTP v1 message body for a single device token. Ready for use once
        /// <see cref="GetAccessTokenAsync"/> exists.</summary>
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
