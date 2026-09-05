using System.Text.Json;
using api.Commands;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>Covers roadmap #241 - a relay must only forward entries for devices in its own tenant, never cross a tenant boundary.</summary>
public class RelayApiControllerTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    private RelayApiController NewController(string callerApiId)
    {
        var catalog = FirmwareTestSupport.NewCatalog(_repo.Object);
        var controller = new RelayApiController(_repo.Object, _cache.Object,
            new CommandQueueService(_repo.Object, _repo.Object, _repo.Object), catalog,
            new api.Devices.DeviceConfigBuilder(_repo.Object, catalog));
        var http = new DefaultHttpContext();
        http.Items[DeviceAuth.ApiIdItemKey] = callerApiId;
        controller.ControllerContext = new() { HttpContext = http };
        return controller;
    }

    private static JsonElement EmptyPayload() => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task Batch_DeviceInDifferentTenant_RejectsEntryWithoutForwarding()
    {
        var relay = new Device { IDDevice = 1, ApiId = "relay1", IsRelay = true, TenantID = 1 };
        var device = new Device { IDDevice = 2, ApiId = "dev1", ApiKey = "key1", TenantID = 2 };
        _repo.Setup(r => r.DeviceGetByApiIdAsync("relay1")).ReturnsAsync(relay);
        _repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(device);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { RelayEnabled = true });

        var controller = NewController("relay1");
        var response = await controller.Batch(new RelayBatchRequest
        {
            Entries = [new RelayBatchEntry { DeviceApiId = "dev1", DeviceApiKey = "key1", Type = RelayEntryType.Event, Payload = EmptyPayload() }]
        });

        var result = Assert.Single(Assert.IsType<RelayBatchResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value).Results);
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        // Strict mock: an un-set-up EventDevicePushAsync call would throw, proving the cross-tenant entry was never forwarded.
    }

    [Fact]
    public async Task Batch_DeviceInSameTenant_Forwards()
    {
        var relay = new Device { IDDevice = 1, ApiId = "relay1", IsRelay = true, TenantID = 1 };
        var device = new Device { IDDevice = 2, ApiId = "dev1", ApiKey = "key1", TenantID = 1 };
        _repo.Setup(r => r.DeviceGetByApiIdAsync("relay1")).ReturnsAsync(relay);
        _repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(device);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { RelayEnabled = true });
        _repo.Setup(r => r.GetCommandByIdAsync(5)).ReturnsAsync((DeviceCommand?)null);

        var payload = JsonDocument.Parse("{\"CommandId\":5}").RootElement;
        var controller = NewController("relay1");
        var response = await controller.Batch(new RelayBatchRequest
        {
            Entries = [new RelayBatchEntry { DeviceApiId = "dev1", DeviceApiKey = "key1", Type = RelayEntryType.CommandAck, Payload = payload }]
        });

        var result = Assert.Single(Assert.IsType<RelayBatchResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value).Results);
        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
    }
}
