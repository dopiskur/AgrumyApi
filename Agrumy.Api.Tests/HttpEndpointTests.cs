using System.Net;
using System.Net.Http.Json;
using api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Agrumy.Api.Tests;

/// Drives the real HTTP middleware pipeline (auth, rate limiting, exception handling) end-to-end instead of unit-testing the pieces in isolation - see roadmap #315.
public sealed class ApiWebApplicationFactory : WebApplicationFactory<api.ApiHostMarker>
{
    // Same JWT values as Agrumy.Api.Tests/appsettings.json (TestConfig's [ModuleInitializer] already
    // called Config.Init with these) so this factory's own Program.cs run re-initializes Config with
    // the identical values instead of racing it under xUnit's parallel test execution.
    public const string SigningKey = "unit-test-signing-key-not-a-secret-0123456789ABCDEF";
    public const string Issuer = "https://tests.agrumy.local";
    public const string Audience = "agrumy-api-tests";

    public ApiWebApplicationFactory()
    {
        // Environment variables are folded into WebApplicationBuilder's configuration at
        // CreateBuilder(args) time, before Program.cs's own pre-Build() DefaultConnection check -
        // unlike WebHostBuilder.ConfigureAppConfiguration, which only applies once Build() runs and
        // would arrive too late to stop that check from routing into the first-boot Setup wizard.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "server=127.0.0.1;port=1;database=agrumy_http_tests;user id=test;password=test;Connection Timeout=2;");
        Environment.SetEnvironmentVariable("JWT__SecureKey", SigningKey);
        Environment.SetEnvironmentVariable("JWT__Issuer", Issuer);
        Environment.SetEnvironmentVariable("JWT__Audience", Audience);
        Environment.SetEnvironmentVariable("WebView__ApiService", "http://127.0.0.1:1");
    }

    public string TokenFor(params string[] roles) =>
        JwtTokenProvider.CreateToken(SigningKey, expiration: 5, subject: "http-tests@example.com", roles, tenantID: "0", Issuer, Audience);
}

public sealed class HttpEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public HttpEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ProtectedEndpoint_NoToken_Returns401()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/Tenant/All");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_TokenWithoutRequiredRole_Returns403()
    {
        using HttpClient client = _factory.CreateClient();
        // "Tenant reader" is a real role, just not one RoleNames.TenantReaders' [Authorize(Roles=...)] accepts.
        client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.TokenFor(RoleNames.TenantReader));

        HttpResponseMessage response = await client.GetAsync("/api/Tenant/All");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReachableWithoutAuth()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// Role check passes, so the request reaches the controller action and its DB call fails against
    /// the deliberately-unreachable connection string above - DbExceptionFilter must turn that into a
    /// structured error response, never an unhandled-exception crash.
    [Fact]
    public async Task DbUnavailable_ReturnsStructuredError_NotUnhandledException()
    {
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.TokenFor(RoleNames.GlobalAdmin));

        HttpResponseMessage response = await client.GetAsync("/api/Tenant/All");

        Assert.True((int)response.StatusCode is 500 or 503, $"Expected a DbExceptionFilter status, got {(int)response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("reason") && body.ContainsKey("message"));
    }

    [Fact]
    public async Task LoginRateLimit_RejectsAfterFivePerMinutePerIp()
    {
        using HttpClient client = _factory.CreateClient();
        HttpStatusCode? sixth = null;

        for (int i = 0; i < 6; i++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync("/api/User/ResendActivation", new { login = $"nobody{i}@example.com" });
            if (i == 5) sixth = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth);
    }
}
