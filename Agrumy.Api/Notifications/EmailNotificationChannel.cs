using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace api.Notifications
{
    /// SMTP email delivery via MailKit. Configured under <c>Notifications:Email</c>.
    public sealed class EmailNotificationChannel : INotificationChannel
    {
        private readonly EmailChannelOptions _options;
        private readonly ILogger<EmailNotificationChannel> _logger;

        public EmailNotificationChannel(IOptions<NotificationOptions> options, ILogger<EmailNotificationChannel> logger)
        {
            _options = options.Value.Email;
            _logger = logger;
        }

        public string Name => "email";

        public bool IsConfigured =>
            _options.Enabled
            && !string.IsNullOrWhiteSpace(_options.Host)
            && !string.IsNullOrWhiteSpace(_options.FromAddress);

        public async Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return NotificationResult.Skipped("email channel disabled or missing Host/FromAddress");
            }
            if (string.IsNullOrWhiteSpace(notification.Recipient.Email))
            {
                return NotificationResult.Skipped("recipient has no email address");
            }

            var message = BuildMessage(notification);

            try
            {
                using var client = new SmtpClient();
                var socketOptions = _options.UseStartTls
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.SslOnConnect;
                await client.ConnectAsync(_options.Host!, _options.Port, socketOptions, ct); // Host non-null: IsConfigured

                if (!string.IsNullOrEmpty(_options.Username))
                {
                    await client.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);
                }

                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                return NotificationResult.Ok($"sent to {notification.Recipient.Email}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email notification to {Recipient} failed.", notification.Recipient.Email);
                return NotificationResult.Failed(ex.Message);
            }
        }

        /// Callers pass a notification with a non-empty recipient email; FromAddress is validated by <see cref="IsConfigured"/> on the send path.
        internal MimeMessage BuildMessage(Notification notification)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress ?? ""));
            message.To.Add(MailboxAddress.Parse(notification.Recipient.Email ?? ""));
            message.Subject = notification.Subject;
            message.Body = new TextPart("plain") { Text = notification.Body };
            return message;
        }
    }
}
