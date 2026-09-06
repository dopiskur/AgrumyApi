using System.Net;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Agrumy.Api.Tests;

/// Covers config gating, the "skip, never throw" contract, message building, and dispatcher fan-out. No real SMTP/FCM traffic.
public class NotificationTests
{
    private static IOptions<NotificationOptions> Opts(NotificationOptions o) => Options.Create(o);

    private static Notification Sample(string? email = "grower@example.com", IReadOnlyList<string>? tokens = null) =>
        new("Low water level", "Tank on device 12 is below the configured minimum.",
            new NotificationRecipient(email, tokens), NotificationSeverity.Warning);

    /// Email's config now lives in ServerConfig (DB), read fresh via IRepository.ServerConfigGetAsync(1) - see EmailNotificationChannel's class remarks.
    private static EmailNotificationChannel Email(ServerConfig config)
    {
        var repo = new Mock<IRepository>(MockBehavior.Strict);
        repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(config);
        return new EmailNotificationChannel(repo.Object, NullLogger<EmailNotificationChannel>.Instance);
    }

    private static FcmPushNotificationChannel Fcm(PushChannelOptions push) =>
        new(Opts(new NotificationOptions { Push = push }), NullLogger<FcmPushNotificationChannel>.Instance);

    private static WebhookNotificationChannel Webhook(WebhookChannelOptions webhook, IHttpClientFactory? factory = null) =>
        new(Opts(new NotificationOptions { Webhook = webhook }), factory ?? new FakeHttpClientFactory(HttpStatusCode.OK), NullLogger<WebhookNotificationChannel>.Instance);


    [Fact]
    public async Task Email_IsConfigured_False_When_Disabled()
    {
        var ch = Email(new ServerConfig { EmailEnabled = false, EmailHost = "smtp.x", EmailFromAddress = "a@x" });
        Assert.False(await ch.IsConfiguredAsync());
    }

    [Theory]
    [InlineData(null, "a@x")]
    [InlineData("smtp.x", null)]
    [InlineData("  ", "a@x")]
    public async Task Email_IsConfigured_False_When_Missing_Host_Or_From(string? host, string? from)
    {
        var ch = Email(new ServerConfig { EmailEnabled = true, EmailHost = host, EmailFromAddress = from });
        Assert.False(await ch.IsConfiguredAsync());
    }

    [Fact]
    public async Task Email_IsConfigured_True_When_Complete()
    {
        var ch = Email(new ServerConfig { EmailEnabled = true, EmailHost = "smtp.x", EmailFromAddress = "a@x" });
        Assert.True(await ch.IsConfiguredAsync());
    }

    [Fact]
    public async Task Email_SendAsync_Skips_When_Not_Configured()
    {
        var result = await Email(new ServerConfig()).SendAsync(Sample());
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
    }

    [Fact]
    public async Task Email_SendAsync_Skips_When_Recipient_Has_No_Email()
    {
        var ch = Email(new ServerConfig { EmailEnabled = true, EmailHost = "smtp.x", EmailFromAddress = "a@x" });
        var result = await ch.SendAsync(Sample(email: null));
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
        Assert.Contains("no email", result.Detail);
    }

    [Fact]
    public void Email_BuildMessage_Sets_From_To_Subject_Body()
    {
        var config = new ServerConfig { EmailFromAddress = "alerts@agrumy.com", EmailFromName = "Agrumy Alerts" };
        var msg = EmailNotificationChannel.BuildMessage(Sample(), config);

        Assert.Equal("alerts@agrumy.com", ((MimeKit.MailboxAddress)msg.From[0]).Address);
        Assert.Equal("Agrumy Alerts", ((MimeKit.MailboxAddress)msg.From[0]).Name);
        Assert.Equal("grower@example.com", ((MimeKit.MailboxAddress)msg.To[0]).Address);
        Assert.Equal("Low water level", msg.Subject);
        Assert.Contains("below the configured minimum", msg.TextBody);
    }


    [Fact]
    public async Task Fcm_IsConfigured_False_By_Default()
    {
        Assert.False(await Fcm(new PushChannelOptions()).IsConfiguredAsync());
    }

    [Fact]
    public async Task Fcm_IsConfigured_False_Even_When_Enabled_Without_Credentials()
    {
        var ch = Fcm(new PushChannelOptions { Enabled = true, FcmProjectId = "agrumy", FcmCredentialsPath = "/no/such/file.json" });
        Assert.False(await ch.IsConfiguredAsync());
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


    [Fact]
    public async Task Dispatcher_With_No_Configured_Channels_Returns_All_Skipped_And_Does_Not_Throw()
    {
        var dispatcher = new NotificationDispatcher(
            new INotificationChannel[] { Email(new ServerConfig()), Fcm(new PushChannelOptions()) },
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
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }


    [Fact]
    public async Task Webhook_IsConfigured_False_When_Disabled()
    {
        var ch = Webhook(new WebhookChannelOptions { Enabled = false, Url = "https://example.com/hook" });
        Assert.False(await ch.IsConfiguredAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://example.com/hook")] // not https
    [InlineData("not a url")]
    public async Task Webhook_IsConfigured_False_When_Url_Missing_Or_Not_Https(string? url)
    {
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = url });
        Assert.False(await ch.IsConfiguredAsync());
    }

    [Fact]
    public async Task Webhook_IsConfigured_True_When_Enabled_With_Https_Url()
    {
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://example.com/hook" });
        Assert.True(await ch.IsConfiguredAsync());
    }

    [Fact]
    public async Task Webhook_SendAsync_Skips_When_Not_Configured()
    {
        var result = await Webhook(new WebhookChannelOptions()).SendAsync(Sample());
        Assert.False(result.Sent);
        Assert.False(result.Attempted);
    }

    [Fact]
    public async Task Webhook_SendAsync_Skips_SecretBearingNotification_EvenWhenConfigured()
    {
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://example.com/hook" }, factory);
        var secretNotification = Sample() with { ContainsSecret = true };

        var result = await ch.SendAsync(secretNotification);

        Assert.False(result.Sent);
        Assert.False(result.Attempted);
        Assert.Null(factory.Handler.LastRequest);
    }

    [Fact]
    public async Task Webhook_SendAsync_Blocked_By_SsrfGuard_For_Loopback_Url()
    {
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://localhost/hook" });
        var result = await ch.SendAsync(Sample());
        Assert.False(result.Sent);
        Assert.True(result.Attempted);
        Assert.Contains("private/reserved", result.Detail);
    }

    [Fact]
    public async Task Webhook_SendAsync_Posts_Json_And_Returns_Ok_On_Success()
    {
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://example.com/hook" }, factory);

        var result = await ch.SendAsync(Sample());

        Assert.True(result.Sent);
        Assert.Equal(HttpMethod.Post, factory.Handler.LastRequest?.Method);
        Assert.Contains("Low water level", factory.Handler.LastRequestBody);
        Assert.Equal("application/json", factory.Handler.LastRequest?.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Webhook_SendAsync_Returns_Failed_On_NonSuccess_StatusCode()
    {
        var factory = new FakeHttpClientFactory(HttpStatusCode.InternalServerError);
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://example.com/hook" }, factory);

        var result = await ch.SendAsync(Sample());

        Assert.False(result.Sent);
        Assert.True(result.Attempted);
    }

    [Fact]
    public async Task Webhook_SendAsync_Adds_Signature_Header_When_Secret_Configured()
    {
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var ch = Webhook(new WebhookChannelOptions { Enabled = true, Url = "https://example.com/hook", Secret = "shh" }, factory);

        await ch.SendAsync(Sample());

        Assert.True(factory.Handler.LastRequest!.Headers.Contains("X-Agrumy-Signature"));
    }

    [Fact]
    public void Webhook_ComputeSignature_Is_Deterministic_And_Depends_On_Secret()
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes("{\"a\":1}");
        string sigA = WebhookNotificationChannel.ComputeSignature(body, "secret-a");
        string sigA2 = WebhookNotificationChannel.ComputeSignature(body, "secret-a");
        string sigB = WebhookNotificationChannel.ComputeSignature(body, "secret-b");

        Assert.Equal(sigA, sigA2);
        Assert.NotEqual(sigA, sigB);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public FakeHttpMessageHandler Handler { get; }
        public FakeHttpClientFactory(HttpStatusCode statusCode) => Handler = new FakeHttpMessageHandler(statusCode);
        public HttpClient CreateClient(string name) => new(Handler);
    }

    /// Captures the last request so a test can assert on method/body/headers without any real network call.
    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return new HttpResponseMessage(statusCode);
        }
    }
}
