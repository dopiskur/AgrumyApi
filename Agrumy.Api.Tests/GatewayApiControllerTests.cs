using System.Security.Claims;
using System.Text.Json;
using api.Commands;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Agrumy.Api.Tests;

/// A gateway must only forward entries for devices in its own tenant, never cross a tenant boundary.
public class GatewayApiControllerTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    private GatewayApiController NewController(string callerApiId)
    {
        var controller = NewJwtController();
        var http = new DefaultHttpContext();
        http.Items[DeviceAuth.ApiIdItemKey] = callerApiId;
        controller.ControllerContext = new() { HttpContext = http };
        return controller;
    }

    private GatewayApiController NewJwtController()
    {
        var catalog = FirmwareTestSupport.NewCatalog(_repo.Object);
        return new GatewayApiController(_repo.Object, _cache.Object,
            new CommandQueueService(_repo.Object, _repo.Object, _repo.Object, new NoOpMqttCommandPublisher()), catalog,
            new api.Devices.DeviceConfigBuilder(_repo.Object, catalog));
    }

    /// Gives a bare (non-DI-constructed) controller the JWT claims an [Authorize] action reads via HttpContext.User - same pattern as ApiControllerTests.SetCallerRoles.
    private static void SetCallerRoles(ControllerBase controller, int? tenantId, params string[] roles)
    {
        var claims = new List<Claim> { new("TenantID", tenantId.ToString() ?? "") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    private static JsonElement EmptyPayload() => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task Batch_DeviceInDifferentTenant_RejectsEntryWithoutForwarding()
    {
        var gateway = new Device { IDDevice = 1, ApiId = "gateway1", IsGateway = true, TenantID = 1 };
        var device = new Device { IDDevice = 2, ApiId = "dev1", ApiKey = "key1", TenantID = 2 };
        _repo.Setup(r => r.DeviceGetByApiIdAsync("gateway1")).ReturnsAsync(gateway);
        _repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(device);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { GatewayEnabled = true });

        var controller = NewController("gateway1");
        var response = await controller.Batch(new GatewayBatchRequest
        {
            Entries = [new GatewayBatchEntry { DeviceApiId = "dev1", DeviceApiKey = "key1", Type = GatewayEntryType.Event, Payload = EmptyPayload() }]
        });

        var result = Assert.Single(Assert.IsType<GatewayBatchResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value).Results);
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        // Strict mock: an un-set-up EventDevicePushAsync call would throw, proving the cross-tenant entry was never forwarded.
    }

    [Fact]
    public async Task Batch_DeviceInSameTenant_Forwards()
    {
        var gateway = new Device { IDDevice = 1, ApiId = "gateway1", IsGateway = true, TenantID = 1 };
        var device = new Device { IDDevice = 2, ApiId = "dev1", ApiKey = "key1", TenantID = 1 };
        _repo.Setup(r => r.DeviceGetByApiIdAsync("gateway1")).ReturnsAsync(gateway);
        _repo.Setup(r => r.DeviceGetByApiIdAsync("dev1")).ReturnsAsync(device);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { GatewayEnabled = true });
        _repo.Setup(r => r.GetCommandByIdAsync(5)).ReturnsAsync((DeviceCommand?)null);

        var payload = JsonDocument.Parse("{\"CommandId\":5}").RootElement;
        var controller = NewController("gateway1");
        var response = await controller.Batch(new GatewayBatchRequest
        {
            Entries = [new GatewayBatchEntry { DeviceApiId = "dev1", DeviceApiKey = "key1", Type = GatewayEntryType.CommandAck, Payload = payload }]
        });

        var result = Assert.Single(Assert.IsType<GatewayBatchResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value).Results);
        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task DeviceMappingAdd_CallerFromDifferentTenantThanGateway_Returns403_NeverCallsRepo()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(1)).ReturnsAsync(new Device { IDDevice = 1, IsGateway = true, TenantID = 1 });

        var controller = NewJwtController();
        SetCallerRoles(controller, 2, RoleNames.TenantDevice); // caller manages tenant 2, gateway belongs to tenant 1
        var result = await controller.DeviceMappingAdd(new GatewayDeviceMapping { IDGatewayDevice = 1, DevEUI = "ABCDEF0123456789", IDDevice = 5 });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up GatewayDeviceMappingAddAsync call would throw, proving no mapping was attempted.
    }

    [Fact]
    public async Task DeviceMappingAdd_GlobalAdminFromDifferentTenant_PassesGatewaysOwnTenantId_NotCallersOwn()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(1)).ReturnsAsync(new Device { IDDevice = 1, IsGateway = true, TenantID = 1 });
        _repo.Setup(r => r.GatewayDeviceMappingAddAsync(1, "ABCDEF0123456789", 5, 1)).ReturnsAsync(true);

        var controller = NewJwtController();
        // GlobalAdmin's own tenant (0) legitimately crosses the gateway-ownership check, but the gateway's OWN tenant (1) - not the caller's - must be what's passed down for the device-tenant guard.
        SetCallerRoles(controller, 0, RoleNames.GlobalAdmin);
        var result = await controller.DeviceMappingAdd(new GatewayDeviceMapping { IDGatewayDevice = 1, DevEUI = "abcdef0123456789", IDDevice = 5 });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.GatewayDeviceMappingAddAsync(1, "ABCDEF0123456789", 5, 1), Times.Once);
    }

    [Fact]
    public async Task DeviceMappingGetAll_CallerFromDifferentTenantThanGateway_Returns403()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(1)).ReturnsAsync(new Device { IDDevice = 1, IsGateway = true, TenantID = 1 });

        var controller = NewJwtController();
        SetCallerRoles(controller, 2, RoleNames.TenantDevice);
        var result = await controller.DeviceMappingGetAll(1);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task DeviceMappingDelete_CallerFromDifferentTenantThanGateway_Returns403_NeverCallsRepo()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(1)).ReturnsAsync(new Device { IDDevice = 1, IsGateway = true, TenantID = 1 });

        var controller = NewJwtController();
        SetCallerRoles(controller, 2, RoleNames.TenantDevice);
        var result = await controller.DeviceMappingDelete(idGatewayDeviceMapping: 9, idGatewayDevice: 1);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }
}
