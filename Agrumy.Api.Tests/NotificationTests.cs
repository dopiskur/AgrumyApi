using api.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agrumy.Api.Tests;

/// <summary>
/// Unit tests for the roadmap #6 notification channels. Covers config gating, the "skip, never
/// throw" contract, message building, and dispatcher fan-out. No real SMTP / FCM traffic.
/// </summary>
public class NotificationTests
{
    private static IOptions<NotificationOptions> Opts(NotificationOptions o) => Options.Create(o);

    private static Notification Sample(string? email = "grower@example.com", IReadOnlyList<string>? tokens = null) =>
        new("Low water level", "Tank on device 12 is below the configured minimum.",
            new NotificationRecipient(email, tokens), NotificationSeverity.Warning);

    private static EmailNotificationChannel Email(EmailChannelOptions email) =>
        new(Opts(new NotificationOptions { Email = email }), NullLogger<EmailNotificationChannel>.Instance);

    private static FcmPushNotificationChannel Fcm(PushChannelOptions push) =>
        new(Opts(new NotificationOptions { Push = push }), NullLogger<FcmPushNotificationChannel>.Instance);

    // ---- Email --------------------------------------------------------------

    [Fact]
    public void Email_IsConfigured_False_When_Disabled()
    {
        var ch = Email(new EmailChannelOptions { Enabled = false, Host = "smtp.x", FromAddress = "a@x" });
        Assert.False(ch.IsConfigured);
    }

    [Theory]
    [InlineData(null, "a@x")]
    [InlineData("smtp.x", null)]
    [InlineData("  ", "a@x")]
    public void Email_IsConfigured_False_When_Missing_Host_Or_From(string? host, string? from)
    {
        var ch = Email(new EmailChannelOptions { Enabled = true, Host = host, FromAddress = from });
        Assert.False(ch.IsConfigured);
    }

    [Fact]
    public void Email_IsConfigured_True_When_Complete()
    {
        var ch = Email(new EmailChannelOptions { Enabled = true, Host = "smtp.x", FromAddress = "a@x" });
        Assert.True(ch.IsConfigured);
    }

    [Fact]
    public async Task Email_SendAsync_Skips_When_Not_Configured()
    {
        var result = await Email(new EmailChannelOptions()).SendAsync(Sample());
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
    }

    [Fact]
    public async Task Email_SendAsync_Skips_When_Recipient_Has_No_Email()
    {
        var ch = Email(new EmailChannelOptions { Enabled = true, Host = "smtp.x", FromAddress = "a@x" });
        var result = await ch.SendAsync(Sample(email: null));
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
        Assert.Contains("no email", result.Detail);
    }

    [Fact]
    public void Email_BuildMessage_Sets_From_To_Subject_Body()
    {
        var ch = Email(new EmailChannelOptions { FromAddress = "alerts@agrumy.com", FromName = "Agrumy Alerts" });
        var msg = ch.BuildMessage(Sample());

        Assert.Equal("alerts@agrumy.com", ((MimeKit.MailboxAddress)msg.From[0]).Address);
        Assert.Equal("Agrumy Alerts", ((MimeKit.MailboxAddress)msg.From[0]).Name);
        Assert.Equal("grower@example.com", ((MimeKit.MailboxAddress)msg.To[0]).Address);
        Assert.Equal("Low water level", msg.Subject);
        Assert.Contains("below the configured minimum", msg.TextBody);
    }

    // ---- FCM push (prepared, inert) --------------------------------------

    [Fact]
    public void Fcm_IsConfigured_False_By_Default()
    {
        Assert.False(Fcm(new PushChannelOptions()).IsConfigured);
    }

    [Fact]
    public void Fcm_IsConfigured_False_Even_When_Enabled_Without_Credentials()
    {
        var ch = Fcm(new PushChannelOptions { Enabled = true, FcmProjectId = "agrumy", FcmCredentialsPath = "/no/such/file.json" });
        Assert.False(ch.IsConfigured);
    }

    [Fact]
    public async Task Fcm_SendAsync_Skips_When_Not_Configured()
    {
        var result = await Fcm(new PushChannelOptions()).SendAsync(Sample(tokens: new[] { "token-abc" }));
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
        Assert.Contains("Android app", result.Detail);
    }

    [Fact]
    public void Fcm_BuildFcmPayload_Contains_Token_Title_Body_Severity()
    {
        var json = FcmPushNotificationChannel.BuildFcmPayload(Sample(), "device-token-xyz");
        Assert.Contains("device-token-xyz", json);
        Assert.Contains("Low water level", json);
        Assert.Contains("below the configured minimum", json);
        Assert.Contains("Warning", json);
    }

    [Fact]
    public void Fcm_SendEndpointFor_Formats_Project()
    {
        Assert.Equal(
            "https://fcm.googleapis.com/v1/projects/agrumy-prod/messages:send",
            FcmPushNotificationChannel.SendEndpointFor("agrumy-prod"));
    }

    // ---- Dispatcher ---------------------------------------------------------

    [Fact]
    public async Task Dispatcher_With_No_Configured_Channels_Returns_All_Skipped_And_Does_Not_Throw()
    {
        var dispatcher = new NotificationDispatcher(
            new INotificationChannel[] { Email(new EmailChannelOptions()), Fcm(new PushChannelOptions()) },
            NullLogger<NotificationDispatcher>.Instance);

        var outcomes = await dispatcher.DispatchAsync(Sample());

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.False(o.Result.Sent));
        Assert.Contains(outcomes, o => o.Channel == "email");
        Assert.Contains(outcomes, o => o.Channel == "push-fcm");
    }

    [Fact]
    public async Task Dispatcher_Isolates_A_Throwing_Channel()
    {
        var dispatcher = new NotificationDispatcher(
            new INotificationChannel[] { new ThrowingChannel() },
            NullLogger<NotificationDispatcher>.Instance);

        var outcomes = await dispatcher.DispatchAsync(Sample());

        var only = Assert.Single(outcomes);
        Assert.False(only.Result.Sent);
        Assert.True(only.Result.Attempted);
        Assert.Equal("boom", only.Result.Detail);
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public string Name => "throwing";
        public bool IsConfigured => true;
        public Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
