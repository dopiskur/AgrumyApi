using api.Dal.Interface;
using api.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace api.Notifications
{
    /// SMTP email delivery via MailKit. Config lives in the DB-backed ServerConfig (Email* fields, admin-editable via Server Settings), not appsettings - read fresh on every call rather than a bound options snapshot, same pattern as api.Commands.MqttCommandPublisher's ServerConfigGetAsync(1) read.
    public sealed class EmailNotificationChannel(IRepository repo, ILogger<EmailNotificationChannel> logger) : INotificationChannel
    {
        public string Name => "email";

        private static bool IsConfigured(ServerConfig config) =>
            config.EmailEnabled
            && !string.IsNullOrWhiteSpace(config.EmailHost)
            && !string.IsNullOrWhiteSpace(config.EmailFromAddress);

        public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
            IsConfigured(await repo.ServerConfigGetAsync(1));

        public async Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            ServerConfig config = await repo.ServerConfigGetAsync(1);
            if (!IsConfigured(config))
            {
                return NotificationResult.Skipped("email channel disabled or missing Host/FromAddress");
            }
            if (string.IsNullOrWhiteSpace(notification.Recipient.Email))
            {
                return NotificationResult.Skipped("recipient has no email address");
            }

            var message = BuildMessage(notification, config);

            try
            {
                using var client = new SmtpClient();
                var socketOptions = config.EmailUseStartTls
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.SslOnConnect;
                await client.ConnectAsync(config.EmailHost!, config.EmailPort, socketOptions, ct); // Host non-null: IsConfigured

                if (!string.IsNullOrEmpty(config.EmailUsername))
                {
                    await client.AuthenticateAsync(config.EmailUsername, config.EmailPassword ?? "", ct);
                }

                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                return NotificationResult.Ok($"sent to {notification.Recipient.Email}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Email notification to {Recipient} failed.", notification.Recipient.Email);
                return NotificationResult.Failed(ex.Message);
            }
        }

        /// Callers pass a notification with a non-empty recipient email; FromAddress is validated by <see cref="IsConfigured"/> on the send path.
        internal static MimeMessage BuildMessage(Notification notification, ServerConfig config)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.EmailFromName, config.EmailFromAddress ?? ""));
            message.To.Add(MailboxAddress.Parse(notification.Recipient.Email ?? ""));
            message.Subject = notification.Subject;
            message.Body = new TextPart("plain") { Text = notification.Body };
            return message;
        }
    }
}
