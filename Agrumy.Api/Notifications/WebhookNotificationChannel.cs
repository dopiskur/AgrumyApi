using System.Security.Cryptography;
using System.Text;
using api.Firmware;
using Microsoft.Extensions.Options;

namespace api.Notifications
{
    /// Posts a JSON event to an operator-configured URL, so an external system learns about an alert without polling Agrumy. Configured under <c>Notifications:Webhook</c>.
    public sealed class WebhookNotificationChannel : INotificationChannel
    {
        public const string ClientName = "WebhookNotificationChannel";

        private readonly WebhookChannelOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WebhookNotificationChannel> _logger;

        public WebhookNotificationChannel(IOptions<NotificationOptions> options, IHttpClientFactory httpClientFactory, ILogger<WebhookNotificationChannel> logger)
        {
            _options = options.Value.Webhook;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "webhook";

        public bool IsConfigured =>
            _options.Enabled
            && Uri.TryCreate(_options.Url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps;

        public async Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return NotificationResult.Skipped("webhook channel disabled or Url missing/not https");
            }

            var uri = new Uri(_options.Url!); // https-scheme, valid absolute: IsConfigured
            try
            {
                await SsrfGuard.EnsureAllowedAsync(uri, ct);
            }
            catch (SsrfBlockedException ex)
            {
                _logger.LogWarning(ex, "Webhook notification blocked by SsrfGuard.");
                return NotificationResult.Failed(ex.Message);
            }

            var payload = new WebhookPayload(notification.Subject, notification.Body, notification.Severity.ToString(), DateTime.UtcNow);
            byte[] body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new ByteArrayContent(body)
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            if (!string.IsNullOrEmpty(_options.Secret))
            {
                request.Headers.Add("X-Agrumy-Signature", ComputeSignature(body, _options.Secret));
            }

            try
            {
                HttpClient client = _httpClientFactory.CreateClient(ClientName);
                using HttpResponseMessage response = await client.SendAsync(request, ct);
                return response.IsSuccessStatusCode
                    ? NotificationResult.Ok($"POST {uri} -> {(int)response.StatusCode}")
                    : NotificationResult.Failed($"POST {uri} -> {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook notification to {Url} failed.", uri);
                return NotificationResult.Failed(ex.Message);
            }
        }

        internal static string ComputeSignature(byte[] body, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
        }

        private sealed record WebhookPayload(string Subject, string Body, string Severity, DateTime TimestampUtc);
    }
}
