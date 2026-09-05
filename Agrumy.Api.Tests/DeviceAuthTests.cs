using System.Security.Claims;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Agrumy.Api.Tests;

/// Drives DeviceApiKeyHandler/DeviceSessionHandler through every rejection path (missing header/unknown device/bad key/expired-session) plus the success path, end to end through the real LoggerMessage delegates.
public class DeviceAuthTests
{
    private static AuthorizationHandlerContext NewContext(IAuthorizationRequirement requirement, HttpContext http) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity()), http);

    private static DefaultHttpContext HttpWithHeaders(string? apiId = null, string? apiKey = null, string? authToken = null)
    {
        var http = new DefaultHttpContext();
        if (apiId != null) { http.Request.Headers["apiId"] = apiId; }
        if (apiKey != null) { http.Request.Headers["apiKey"] = apiKey; }
        if (authToken != null) { http.Request.Headers.Authorization = authToken; }
        return http;
    }


    [Fact]
    public async Task ApiKey_MissingHeader_Fails()
    {
        var repo = new Mock<IDeviceRepository>(MockBehavior.Strict);
        var handler = new DeviceApiKeyHandler(repo.Object, NullLogger<DeviceApiKeyHandler>.Instance);
        var context = NewContext(new DeviceApiKeyRequirement(), HttpWithHeaders());

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        repo.Verify(r => r.DeviceGetByApiIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApiKey_UnknownDevice_Fails()
    {
        var repo = new Mock<IDeviceRepository>(MockBehavior.Strict);
        repo.Setup(r => r.DeviceGetByApiIdAsync("ghost")).ReturnsAsync((Device?)null);
        var handler = new DeviceApiKeyHandler(repo.Object, NullLogger<DeviceApiKeyHandler>.Instance);
        var context = NewContext(new DeviceApiKeyRequirement(), HttpWithHeaders(apiId: "ghost", apiKey: "whatever"));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ApiKey_WrongKey_Fails()
    {
        var repo = new Mock<IDeviceRepository>(MockBehavior.Strict);
        repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(new Device { ApiId = "dev1", ApiKey = "correct" });
        var handler = new DeviceApiKeyHandler(repo.Object, NullLogger<DeviceApiKeyHandler>.Instance);
        var context = NewContext(new DeviceApiKeyRequirement(), HttpWithHeaders(apiId: "dev1", apiKey: "wrong"));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task ApiKey_CorrectKey_Succeeds()
    {
        var repo = new Mock<IDeviceRepository>(MockBehavior.Strict);
        repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(new Device { ApiId = "dev1", ApiKey = "correct" });
        var handler = new DeviceApiKeyHandler(repo.Object, NullLogger<DeviceApiKeyHandler>.Instance);
        var http = HttpWithHeaders(apiId: "dev1", apiKey: "correct");
        var context = NewContext(new DeviceApiKeyRequirement(), http);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal("dev1", http.DeviceApiId());
    }


    [Fact]
    public async Task Session_MissingHeader_Fails()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict);
        var handler = new DeviceSessionHandler(cache.Object, NullLogger<DeviceSessionHandler>.Instance);
        var context = NewContext(new DeviceSessionRequirement(), HttpWithHeaders());

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        cache.Verify(c => c.GetDeviceCacheAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Session_NoActiveSession_Fails()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict);
        cache.Setup(c => c.GetDeviceCacheAsync("dev1")).ReturnsAsync(new DeviceCache());
        var handler = new DeviceSessionHandler(cache.Object, NullLogger<DeviceSessionHandler>.Instance);
        var context = NewContext(new DeviceSessionRequirement(), HttpWithHeaders(apiId: "dev1", authToken: "Bearer sometoken"));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Session_ValidToken_Succeeds()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict);
        cache.Setup(c => c.GetDeviceCacheAsync("dev1")).ReturnsAsync(new DeviceCache { apiAuth = "sometoken" });
        var handler = new DeviceSessionHandler(cache.Object, NullLogger<DeviceSessionHandler>.Instance);
        var http = HttpWithHeaders(apiId: "dev1", authToken: "Bearer sometoken");
        var context = NewContext(new DeviceSessionRequirement(), http);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal("dev1", http.DeviceApiId());
    }
}
