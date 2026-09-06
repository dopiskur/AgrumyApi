using System.Security.Claims;
using System.Text.Json.Nodes;
using api;
using api.BackgroundWorkers;
using api.Commands;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Agrumy.Api.Tests;

/// Controller tests with a mocked <see cref="IRepository"/>/<see cref="ICache"/>, bypassing the MVC pipeline (DbExceptionFilter behavior is covered by <see cref="DbExceptionFilterTests"/>).
public class ApiControllerTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    private readonly Mock<INotificationDispatcher> _notifications = new();
    private readonly BackgroundJobQueue _jobQueue = new();

    // Bound from the same appsettings.json TestConfig.Init() reads Config.* from, so a token signed here and JwtTokenProvider.ValidateToken use the same key/issuer/audience.
    private static readonly IOptions<AgrumySettings> TestSettings = Options.Create(AgrumySettings.Bind(TestConfig.Configuration));

    // CommandQueueService is a plain sealed class (not mocked); IRepository already implements all three interfaces it needs, so one mock backs all three constructor params.
    private DeviceApiController NewDeviceController()
    {
        var catalog = FirmwareTestSupport.NewCatalog(_repo.Object);
        return new(_repo.Object, _cache.Object,
            new CommandQueueService(_repo.Object, _repo.Object, _repo.Object, new NoOpMqttCommandPublisher()), catalog,
            new api.Devices.DeviceConfigBuilder(_repo.Object, catalog), TestSettings);
    }
    private UserApiController NewUserController() => new(_repo.Object, _cache.Object, _jobQueue, TestSettings);
    private DeviceCommandApiController NewDeviceCommandController() =>
        new(_repo.Object, _cache.Object, new CommandQueueService(_repo.Object, _repo.Object, _repo.Object, new NoOpMqttCommandPublisher()));

    /// UserApiController enqueues notification jobs instead of dispatching them inline (roadmap #305) - this runs the one job a test expects to have been queued against a fake scope resolving the same mocks, then lets the test assert on _notifications/_repo as before.
    private async Task RunOneQueuedJobAsync()
    {
        Assert.True(_jobQueue.Reader.TryRead(out var job), "Expected a background job to have been enqueued.");
        IServiceProvider services = new ServiceCollection()
            .AddSingleton(_notifications.Object)
            .AddSingleton(_repo.Object)
            .BuildServiceProvider();
        await job(services, CancellationToken.None);
    }

    private void AssertNoJobWasQueued() =>
        Assert.False(_jobQueue.Reader.TryRead(out _), "Expected no background job to have been enqueued.");
    private DeviceUnitApiController NewDeviceUnitController() => new(_repo.Object, _cache.Object, TestSettings);
    private TenantApiController NewTenantController() => new(_repo.Object, _cache.Object,
        new api.Migration.TenantExportService(_repo.Object), new api.Migration.TenantImportService(_repo.Object));

    /// Gives a bare (non-DI-constructed) controller the JWT claims an [Authorize] action reads via HttpContext.User.
    private static void SetCaller(ControllerBase controller, string role, int? tenantId) =>
        SetCallerRoles(controller, tenantId, role);

    /// Same, but with the full multi-role claim set a real token carries (legacy alias first, then granular roles - order matters only for CallerRole).
    private static void SetCallerRoles(ControllerBase controller, int? tenantId, params string[] roles)
    {
        var claims = new List<Claim> { new("TenantID", tenantId.ToString() ?? "") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }


    [Fact]
    public async Task DeviceGet_HappyPath_ReturnsOkWithDevice()
    {
        var device = new Device { IDDevice = 42, DeviceName = "greenhouse-1" };
        _repo.Setup(r => r.DeviceGetAsync(7, 42, null, null)).ReturnsAsync(device);

        var controller = NewDeviceController();
        SetCaller(controller, "user", 7); // DeviceGet scopes to the caller's tenant
        var result = await controller.DeviceGet(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<DeviceDto>(ok.Value);
        Assert.Equal(device.IDDevice, dto.IDDevice);
        Assert.Equal(device.DeviceName, dto.DeviceName);
    }

    [Fact]
    public async Task DeviceDelete_DifferentTenant_Returns403AndDoesNotCallDelete()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 99 });

        var controller = NewDeviceController();
        SetCaller(controller, "admin", 1);
        var result = await controller.DeviceDelete(7);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.DeviceDeleteAsync(It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task DeviceUpdate_UnknownDevice_Returns404AndDoesNotUpdate()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(99)).ReturnsAsync((Device?)null);

        var controller = NewDeviceController();
        SetCaller(controller, "admin", 1);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 99 });

        Assert.IsType<NotFoundResult>(result.Result);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Never);
    }

    [Fact]
    public async Task DeviceGet_UnknownId_Returns404()
    {
        _repo.Setup(r => r.DeviceGetAsync(1, 99, null, null)).ReturnsAsync((Device?)null);

        var controller = NewDeviceController();
        SetCaller(controller, "user", 1);
        var result = await controller.DeviceGet(99);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeviceConfigSensorGet_DifferentTenant_Returns403_WithConfigSpecificMessage()
    {
        _repo.Setup(r => r.DeviceGetByDeviceConfigSensorIdAsync(5))
             .ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceController();
        SetCaller(controller, "user", 1);
        var result = await controller.DeviceConfigSensorGet(5);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        Assert.Equal("Sensor config belongs to a different tenant", obj.Value);
        _repo.Verify(r => r.DeviceConfigSensorGetAsync(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task DeviceConfigSensorGet_OwnedByCaller_ReturnsConfig()
    {
        var cfg = new DeviceConfigSensor { IDDeviceConfigSensor = 5, SensorTemp = 1 };
        _repo.Setup(r => r.DeviceGetByDeviceConfigSensorIdAsync(5))
             .ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });
        _repo.Setup(r => r.DeviceConfigSensorGetAsync(5)).ReturnsAsync(cfg);

        var controller = NewDeviceController();
        SetCaller(controller, "user", 1);
        var result = await controller.DeviceConfigSensorGet(5);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(cfg, ok.Value);
    }

    [Fact]
    public async Task DeviceConfigSensorGet_TenantDataReaderOnly_Returns403_NeverLooksUpDevice()
    {
        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDataReader);
        var result = await controller.DeviceConfigSensorGet(5);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up DeviceGetByDeviceConfigSensorIdAsync call would throw, proving it never runs.
    }

    [Fact]
    public async Task DeviceConfigControllerGet_GlobalDataReaderOnly_Returns403_NeverLooksUpDevice()
    {
        var controller = NewDeviceController();
        SetCallerRoles(controller, 0, "user", RoleNames.GlobalDataReader);
        var result = await controller.DeviceConfigControllerGet(5);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up DeviceGetByDeviceConfigControllerIdAsync call would throw, proving it never runs.
    }

    [Fact]
    public async Task DeviceConfigSensorGet_TenantDataReaderPlusTenantReader_NotBlocked()
    {
        // Holding a broader role alongside Data Reader must not be MORE restrictive than the broader role alone.
        _repo.Setup(r => r.DeviceGetByDeviceConfigSensorIdAsync(5)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });
        _repo.Setup(r => r.DeviceConfigSensorGetAsync(5)).ReturnsAsync(new DeviceConfigSensor { IDDeviceConfigSensor = 5 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDataReader);
        var result = await controller.DeviceConfigSensorGet(5);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeviceUnitZoneRulesGet_TenantDataReaderOnly_Returns403_NeverLooksUpZone()
    {
        var controller = NewDeviceUnitController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDataReader);
        var result = await controller.DeviceUnitZoneRulesGet(5);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up DeviceUnitZoneGetByIdAsync call would throw, proving it never runs.
    }

    [Fact]
    public async Task DeviceAssign_GlobalAdmin_CrossTenantDeviceAndZone_Returns403_NeverAssigns()
    {
        // GlobalAdmin legitimately crosses tenants for the device AND the zone's own ownership checks, but the device and zone still belong to different tenants from each other.
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });
        _repo.Setup(r => r.DeviceUnitZoneGetByIdAsync(5)).ReturnsAsync(new DeviceUnitZone { IDDeviceUnitZone = 5, TenantID = 2 });

        var controller = NewDeviceUnitController();
        SetCallerRoles(controller, 0, "admin", RoleNames.GlobalAdmin);
        var result = await controller.DeviceAssign(new DeviceZoneAssignment { IDDevice = 8, IDDeviceUnitZone = 5 });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up DeviceUnitZoneHasControllerAsync/DeviceAssignToZoneAsync call would throw, proving the assignment never happened.
    }

    [Fact]
    public async Task DeviceAssign_SameTenantDeviceAndZone_Succeeds()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1, DeviceControllerEnabled = false });
        _repo.Setup(r => r.DeviceUnitZoneGetByIdAsync(5)).ReturnsAsync(new DeviceUnitZone { IDDeviceUnitZone = 5, TenantID = 1 });
        _repo.Setup(r => r.DeviceAssignToZoneAsync(8, 5)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewDeviceUnitController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDevice);
        var result = await controller.DeviceAssign(new DeviceZoneAssignment { IDDevice = 8, IDDeviceUnitZone = 5 });

        Assert.True(result.Value);
        _repo.Verify(r => r.DeviceAssignToZoneAsync(8, 5), Times.Once);
    }

    [Fact]
    public async Task UsersGet_TenantDataReaderOnly_Returns403()
    {
        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDataReader);
        var result = await controller.UsersGet();

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up UsersGetAsync call would throw, proving it never runs.
    }

    [Fact]
    public async Task UserGet_GlobalDataReaderOnly_Returns403()
    {
        var controller = NewUserController();
        SetCallerRoles(controller, 0, "user", RoleNames.GlobalDataReader);
        var result = await controller.UserGet(7);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up UserGetAsync call would throw, proving it never runs.
    }

    [Fact]
    public async Task GetUserSelf_TenantDataReaderOnly_StillAllowed()
    {
        // Viewing one's OWN account is not "reading user accounts" in the roadmap #282 sense.
        _repo.Setup(r => r.UserGetAsync(null, "reader@test.local", null)).ReturnsAsync(new User { IDUser = 9, Email = "reader@test.local" });

        var controller = NewUserController();
        var claims = new List<Claim> { new("TenantID", "1"), new(ClaimTypes.Name, "reader@test.local"), new(ClaimTypes.Role, RoleNames.TenantDataReader) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
        var result = await controller.GetUserSelf();

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public void RoleNames_DataReaders_AreRegisteredAndScopedToMetrics()
    {
        Assert.Contains(RoleNames.GlobalDataReader, RoleNames.All);
        Assert.Contains(RoleNames.TenantDataReader, RoleNames.All);
        Assert.Contains(RoleNames.GlobalDataReader, RoleNames.MetricsReaders);
        Assert.Contains(RoleNames.TenantDataReader, RoleNames.MetricsReaders);
        // A Data Reader must never be folded into a role group that can manage devices or users.
        Assert.DoesNotContain(RoleNames.GlobalDataReader, RoleNames.DeviceManagers);
        Assert.DoesNotContain(RoleNames.TenantDataReader, RoleNames.DeviceManagers);
        Assert.DoesNotContain(RoleNames.GlobalDataReader, RoleNames.UserManagers);
        Assert.DoesNotContain(RoleNames.TenantDataReader, RoleNames.UserManagers);
    }

    [Fact]
    public async Task DeviceRegistration_UnknownEmail_Returns401_NotA503()
    {
        _repo.Setup(r => r.UserGetAsync(null, "ghost@example.com", null))
             .ReturnsAsync((User?)null);

        var controller = NewDeviceController();
        var result = await controller.DeviceRegistration(new DeviceRegistration
        {
            Email = "ghost@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
        });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        Assert.Equal("Wrong user or pin", obj.Value);
        _repo.Verify(r => r.DeviceAddAsync(It.IsAny<Device>()), Times.Never);
    }


    [Theory]
    // 60s poll: window is 60*3 + 90 = 270s.
    [InlineData(60, 269, true)]
    [InlineData(60, 271, false)]
    // Slow poller (deep-sleep node): the window scales with SleepSeconds instead of a fixed cutoff.
    [InlineData(3600, 10000, true)]
    [InlineData(3600, 11000, false)]
    // SleepSeconds null falls back to the 60s default; 0 is floored by the grace window.
    [InlineData(null, 269, true)]
    [InlineData(0, 89, true)]
    [InlineData(0, 91, false)]
    public void FleetComputeOnline_Thresholds(int? sleepSeconds, int ageSeconds, bool expected)
    {
        DateTime now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, DeviceFleetStatus.ComputeOnline(now.AddSeconds(-ageSeconds), sleepSeconds, now));
    }

    [Fact]
    public void FleetComputeOnline_NeverSeen_IsOffline() =>
        Assert.False(DeviceFleetStatus.ComputeOnline(null, 60, DateTime.UtcNow));

    [Fact]
    public async Task GetConfig_RecordsHeartbeat_EvenWhenConfigIsUpToDate()
    {
        var controller = NewDeviceController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66, LastFullConfigSentAt = DateTime.UtcNow });
        _repo.Setup(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.IsAny<DeviceConfigPoll>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig()); // NeedsRefreshAsync's heartbeat check
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>()); // none pending

        var result = await controller.GetConfig(new DeviceConfigPoll { ConfigVersion = 66, Rssi = -60 });

        _repo.Verify(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.Is<DeviceConfigPoll>(p => p.Rssi == -60)), Times.Once);
        Assert.IsType<OkResult>(result.Result); // empty body: device is up to date
    }

    // Uses the device row GetConfig already read, not a cache copy - proven by never stubbing Cache (a loose mock would silently return defaults rather than fail) and verifying neither method is called.
    [Fact]
    public async Task GetConfig_NeverTouchesSessionCache()
    {
        var controller = NewDeviceController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66 });
        _repo.Setup(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.IsAny<DeviceConfigPoll>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig()); // BuildDeviceConfigAsync always reads this
        _repo.Setup(r => r.TenantGetByIdAsync(3)).ReturnsAsync(new Tenant { IDTenant = 3 });
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>()); // none pending
        _repo.Setup(r => r.DeviceMarkConfigSentAsync(500, It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.DeviceSimulationGetAsync(500)).ReturnsAsync((DeviceSimulation?)null);

        // Version mismatch must return the full config from the DB read, not a cache lookup that no longer carries ConfigVersion.
        var result = await controller.GetConfig(new DeviceConfigPoll { ConfigVersion = 65 });

        Assert.IsType<OkObjectResult>(result.Result);
        _cache.Verify(c => c.GetDeviceCacheAsync(It.IsAny<string>()), Times.Never);
        _cache.Verify(c => c.SetItemAsync(It.IsAny<string>(), It.IsAny<DeviceCache>()), Times.Never);
    }

    // Roadmap #288: UtcOffsetSeconds/SkipWaterPumpForRain are recomputed fresh on every BuildAsync call but never bump ConfigVersion, so a matching version alone must not be enough to skip a device that hasn't had a full send in longer than ConfigHeartbeatHours.
    [Fact]
    public async Task GetConfig_VersionMatches_ButHeartbeatWindowElapsed_StillSendsFullConfig()
    {
        var controller = NewDeviceController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66, LastFullConfigSentAt = DateTime.UtcNow.AddHours(-25) });
        _repo.Setup(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.IsAny<DeviceConfigPoll>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { ConfigHeartbeatHours = 24 });
        _repo.Setup(r => r.TenantGetByIdAsync(3)).ReturnsAsync(new Tenant { IDTenant = 3 });
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceMarkConfigSentAsync(500, It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.DeviceSimulationGetAsync(500)).ReturnsAsync((DeviceSimulation?)null);

        var result = await controller.GetConfig(new DeviceConfigPoll { ConfigVersion = 66 });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.DeviceMarkConfigSentAsync(500, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task GetConfig_VersionMatches_HeartbeatDisabled_StaysUpToDate()
    {
        var controller = NewDeviceController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66, LastFullConfigSentAt = null });
        _repo.Setup(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.IsAny<DeviceConfigPoll>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { ConfigHeartbeatHours = 0 });
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>());

        var result = await controller.GetConfig(new DeviceConfigPoll { ConfigVersion = 66 });

        Assert.IsType<OkResult>(result.Result); // empty body: 0 disables the heartbeat entirely, even with no prior send recorded
        // Strict mock: an un-set-up DeviceMarkConfigSentAsync call would throw, proving no config was sent.
    }

    // Authenticate must size the session TTL to the device's own SleepSeconds, not a fixed default, or a slow-polling device loses its session mid-cycle.
    [Theory]
    [InlineData(null, 1800)]    // no SleepSeconds on record - 30-min floor
    [InlineData(60, 1800)]      // short poll - 2x60=120s would be far too short, floor applies
    [InlineData(3600, 7200)]    // 1h sleep - 2x
    [InlineData(86400, 172800)] // 24h, the #89 dropdown's max option - 2x
    public async Task Authenticate_SizesSessionTtlToDeviceSleepInterval(int? sleepSeconds, int expectedTtlSeconds)
    {
        var controller = NewDeviceController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, SleepSeconds = sleepSeconds });

        TimeSpan? capturedTtl = null;
        _cache.Setup(c => c.SetItemAsync(It.IsAny<string>(), It.IsAny<DeviceCache>(), It.IsAny<TimeSpan?>()))
              .Callback<string, DeviceCache, TimeSpan?>((_, _, ttl) => capturedTtl = ttl)
              .Returns(Task.CompletedTask);

        var result = await controller.ReqAuth();

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(TimeSpan.FromSeconds(expectedTtlSeconds), capturedTtl);
    }


    private static DeviceRegistration PinRegistration(string pin) => new()
    {
        Email = "owner@example.com",
        DevicePin = pin,
        MacAddress = "AABBCCDDEEFF",
    };

    private void StubOwner(string? storedPin, DateTime? expiresAt) =>
        _repo.Setup(r => r.UserGetAsync(null, "owner@example.com", null))
             .ReturnsAsync(new User { IDUser = 77, TenantID = 1, DevicePin = storedPin, DevicePinExpires = expiresAt });

    [Theory]
    [InlineData("ABC234", -5)]  // right PIN, expired 5 minutes ago
    [InlineData("WRONG9", 60)]  // wrong PIN, unexpired
    public async Task DeviceRegistration_ExpiredOrWrongPin_Returns401_WithTheSameGenericMessage(string sentPin, int expiresInMinutes)
    {
        StubOwner("ABC234", DateTime.UtcNow.AddMinutes(expiresInMinutes));

        var result = await NewDeviceController().DeviceRegistration(PinRegistration(sentPin));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        Assert.Equal("Wrong user or pin", obj.Value); // an expired PIN must not confirm the email exists
        _repo.Verify(r => r.DeviceAddAsync(It.IsAny<Device>()), Times.Never);
    }

    [Fact]
    public async Task DeviceRegistration_NoPinEverGenerated_Returns401()
    {
        StubOwner(null, null); // DevicePin/DevicePinExpires default null until the user ever generates one

        var result = await NewDeviceController().DeviceRegistration(PinRegistration("ABC234"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task DeviceRegistration_ValidPin_LowercaseAccepted()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 1, DeviceSensorEnabled = false, DeviceControllerEnabled = false });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig()); // BuildDeviceConfigAsync always reads this now
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>()); // none pending
        _repo.Setup(r => r.DeviceSimulationGetAsync(500)).ReturnsAsync((DeviceSimulation?)null);

        var result = await NewDeviceController().DeviceRegistration(PinRegistration("abc234"));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeviceRegistration_ValidPin_NotConsumed_ReusableForASecondDevice()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 1, DeviceSensorEnabled = false, DeviceControllerEnabled = false });
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "112233445566"))
             .ReturnsAsync(new Device { IDDevice = 501, TenantID = 1, DeviceSensorEnabled = false, DeviceControllerEnabled = false });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig()); // BuildDeviceConfigAsync always reads this now
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(500)).ReturnsAsync(new List<DeviceCommand>()); // none pending
        _repo.Setup(r => r.GetPendingCommandsAsync(501)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceSimulationGetAsync(It.IsAny<int>())).ReturnsAsync((DeviceSimulation?)null);

        var first = await NewDeviceController().DeviceRegistration(PinRegistration("ABC234"));
        var second = await NewDeviceController().DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "112233445566",
        });

        Assert.IsType<OkObjectResult>(first.Result);
        Assert.IsType<OkObjectResult>(second.Result);
        _repo.Verify(r => r.UserSetDevicePinAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    private DeviceApiController NewDeviceControllerWithGatewaySecret(string? serverSecret)
    {
        var catalog = FirmwareTestSupport.NewCatalog(_repo.Object);
        return new(_repo.Object, _cache.Object,
            new CommandQueueService(_repo.Object, _repo.Object, _repo.Object, new NoOpMqttCommandPublisher()), catalog,
            new api.Devices.DeviceConfigBuilder(_repo.Object, catalog),
            Options.Create(new AgrumySettings { GatewayRegistrationSecret = serverSecret }));
    }

    [Fact]
    public async Task DeviceRegistration_IsGateway_CorrectSecret_IsHonored()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF")).ReturnsAsync((Device?)null);
        Device? captured = null;
        _repo.Setup(r => r.DeviceAddAsync(It.IsAny<Device>()))
             .Callback<Device>(d => captured = d)
             .ReturnsAsync((Device d) => { d.IDDevice = 900; return d; });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(900)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceSimulationGetAsync(900)).ReturnsAsync((DeviceSimulation?)null);

        var result = await NewDeviceControllerWithGatewaySecret("shared-secret").DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
            IsGateway = true,
            GatewayProfile = GatewayProfile.WiFiRepeater,
            GatewayRegistrationSecret = "shared-secret",
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(captured!.IsGateway);
        Assert.Equal(GatewayProfile.WiFiRepeater, captured.GatewayProfile);
    }

    [Fact]
    public async Task DeviceRegistration_IsGateway_WrongSecret_SilentlyRegistersAsOrdinaryDevice()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF")).ReturnsAsync((Device?)null);
        Device? captured = null;
        _repo.Setup(r => r.DeviceAddAsync(It.IsAny<Device>()))
             .Callback<Device>(d => captured = d)
             .ReturnsAsync((Device d) => { d.IDDevice = 900; return d; });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(900)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceSimulationGetAsync(900)).ReturnsAsync((DeviceSimulation?)null);

        var result = await NewDeviceControllerWithGatewaySecret("shared-secret").DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
            IsGateway = true,
            GatewayProfile = GatewayProfile.WiFiRepeater,
            GatewayRegistrationSecret = "guessed-wrong",
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(captured!.IsGateway);
        Assert.Null(captured.GatewayProfile);
    }

    [Fact]
    public async Task DeviceRegistration_IsGateway_NoServerSecretConfigured_AlwaysDropsGatewayStatus()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF")).ReturnsAsync((Device?)null);
        Device? captured = null;
        _repo.Setup(r => r.DeviceAddAsync(It.IsAny<Device>()))
             .Callback<Device>(d => captured = d)
             .ReturnsAsync((Device d) => { d.IDDevice = 900; return d; });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(900)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceSimulationGetAsync(900)).ReturnsAsync((DeviceSimulation?)null);

        var result = await NewDeviceControllerWithGatewaySecret(null).DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
            IsGateway = true,
            GatewayProfile = GatewayProfile.WiFiRepeater,
            GatewayRegistrationSecret = "",
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(captured!.IsGateway);
    }

    private void StubNewDeviceRegistration(out Func<Device?> captured)
    {
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF")).ReturnsAsync((Device?)null);
        Device? c = null;
        _repo.Setup(r => r.DeviceAddAsync(It.IsAny<Device>()))
             .Callback<Device>(d => c = d)
             .ReturnsAsync((Device d) => { d.IDDevice = 900; return d; });
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());
        _repo.Setup(r => r.TenantGetByIdAsync(1)).ReturnsAsync(new Tenant { IDTenant = 1 });
        _repo.Setup(r => r.GetPendingCommandsAsync(900)).ReturnsAsync(new List<DeviceCommand>());
        _repo.Setup(r => r.DeviceSimulationGetAsync(900)).ReturnsAsync((DeviceSimulation?)null);
        captured = () => c;
    }

    [Fact]
    public async Task DeviceRegistration_NewDevice_UsesCaptivePortalDisplayName_WhenNoProvisionQueued()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        StubNewDeviceRegistration(out var captured);
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand>());

        var result = await NewDeviceController().DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
            DisplayName = "My Greenhouse",
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("My Greenhouse", captured()!.DeviceName);
    }

    [Fact]
    public async Task DeviceRegistration_NewDevice_FallsBackToGenericName_WhenDisplayNameBlank()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        StubNewDeviceRegistration(out var captured);
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand>());

        var result = await NewDeviceController().DeviceRegistration(PinRegistration("ABC234")); // no DisplayName set

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Agrumy_AABBCCDDEEFF", captured()!.DeviceName);
    }

    [Fact]
    public async Task DeviceRegistration_NewDevice_ProvisionedNameWins_OverCaptivePortalDisplayName()
    {
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        StubNewDeviceRegistration(out var captured);
        var provisionCommand = new DeviceCommand
        {
            IDDeviceCommand = 55,
            Status = CommandStatus.Acknowledged,
            Payload = System.Text.Json.JsonSerializer.Serialize(new DiscoveryProvisionPayload
            {
                Username = "owner@example.com",
                Pin = "ABC234",
                DiscoveredApMac = "AABBCCDDEEFF",
                Ssid = "TestWifi",
                WifiPassword = "pw",
                DeviceName = "Provisioned Greenhouse",
            }),
        };
        _repo.Setup(r => r.GetActiveProvisionCommandsAsync()).ReturnsAsync(new List<DeviceCommand> { provisionCommand });
        _repo.Setup(r => r.SetCommandStatusAsync(55, CommandStatus.Executed, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var result = await NewDeviceController().DeviceRegistration(new DeviceRegistration
        {
            Email = "owner@example.com",
            DevicePin = "ABC234",
            MacAddress = "AABBCCDDEEFF",
            DisplayName = "Ignored Captive-Portal Name",
        });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Provisioned Greenhouse", captured()!.DeviceName);
    }

    /// UserRegistration now delegates tenant-create + user-add + activation-token + starting-role to one transactional Repo.RegisterUserAsync (roadmap #293) - this stubs it to capture what a test needs and mutate `user.TenantID` the same way the real method does, since UserRegistration's own `return Ok(user)` reflects that mutation.
    private void StubRegisterUser(int idUser, Action<User, int?, string?, IReadOnlyList<string>>? capture = null)
    {
        _repo.Setup(r => r.RegisterUserAsync(It.IsAny<User>(), It.IsAny<UserSecret>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>()))
             .Callback<User, UserSecret, int?, string?, string, DateTime, IEnumerable<string>>((u, _, existingTenantId, newTenantName, _, _, roles) =>
             {
                 var roleList = roles.ToList();
                 u.TenantID = existingTenantId ?? 42; // 42 stands in for a freshly created tenant's id
                 capture?.Invoke(u, existingTenantId, newTenantName, roleList);
             })
             .ReturnsAsync(idUser);
    }

    [Fact]
    public async Task UserRegistration_NewTenantName_CreatesTenantAndBecomesAdmin()
    {
        _repo.Setup(r => r.TenantGetAsync("AcmeCorp")).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { AllowSelfServiceTenantCreation = true });

        User? capturedUser = null;
        int? capturedExistingTenantId = null;
        string? capturedNewTenantName = null;
        List<string>? seededRoles = null;
        StubRegisterUser(1, (u, existingTenantId, newTenantName, roles) =>
        {
            capturedUser = u;
            capturedExistingTenantId = existingTenantId;
            capturedNewTenantName = newTenantName;
            seededRoles = roles.ToList();
        });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "TestPass123!", TenantName = "AcmeCorp" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Null(capturedExistingTenantId); // a brand new tenant, not an existing one
        Assert.Equal("AcmeCorp", capturedNewTenantName);
        Assert.Equal(42, capturedUser!.TenantID);
        Assert.Equal(new[] { RoleNames.TenantAdmin }, seededRoles); // admin on a brand new tenant
        Assert.False(capturedUser.Enabled);         // Activate() is what enables, not registration
        Assert.False(capturedUser.EmailVerified);   // still needs to click the activation link
        await RunOneQueuedJobAsync();
        _notifications.Verify(n => n.DispatchAsync(It.Is<Notification>(msg => msg.ContainsSecret), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserRegistration_PasswordTooShort_Returns400AndDoesNotCreateUser()
    {
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { PasswordMinLength = 8 });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "short1", TenantName = "AcmeCorp" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        // Strict mock: an un-set-up TenantGetAsync/UserAddAsync call would throw, proving the policy check ran before touching either.
    }

    [Fact]
    public async Task UserRegistration_UnknownTenantName_SelfServiceDisabled_Returns403AndDoesNotCreateTenant()
    {
        _repo.Setup(r => r.TenantGetAsync("AcmeCorp")).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { AllowSelfServiceTenantCreation = false });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "TestPass123!", TenantName = "AcmeCorp" };
        var result = await controller.UserRegistration(value);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.TenantAddAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UserRegistration_NewTenantName_TooShort_Returns400EvenWhenSelfServiceEnabled()
    {
        _repo.Setup(r => r.TenantGetAsync("abc")).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { AllowSelfServiceTenantCreation = true });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "TestPass123!", TenantName = "abc" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _repo.Verify(r => r.TenantAddAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UserRegistration_ExistingTenantName_JoinsAsDisabledRegularUser()
    {
        _repo.Setup(r => r.TenantGetAsync("Acme")).ReturnsAsync(true);
        _repo.Setup(r => r.TenantGetIdAsync("Acme")).ReturnsAsync(42);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        User? capturedUser = null;
        int? capturedExistingTenantId = null;
        List<string>? seededRoles = null;
        StubRegisterUser(2, (u, existingTenantId, newTenantName, roles) =>
        {
            capturedUser = u;
            capturedExistingTenantId = existingTenantId;
            seededRoles = roles.ToList();
        });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "member@acme.local", Username = "member", Password = "TestPass123!", TenantName = "Acme" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(42, capturedExistingTenantId); // joins the existing tenant, no new one created
        Assert.Equal(42, capturedUser!.TenantID);
        Assert.Equal(new[] { RoleNames.TenantReader }, seededRoles); // regular user, not admin
        Assert.False(capturedUser.Enabled);        // waits for that tenant's admin to enable them
    }

    [Fact]
    public async Task UserRegistration_ExistingTenantZero_StillDisabled_ActivateEnablesInstead()
    {
        // TenantID 0 has no owning admin to ask, but that's Activate()'s decision to make, not registration's - see Activate_TenantZero_* below.
        _repo.Setup(r => r.TenantGetAsync("default")).ReturnsAsync(true);
        _repo.Setup(r => r.TenantGetIdAsync("default")).ReturnsAsync(0);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        User? capturedUser = null;
        StubRegisterUser(3, (u, _, _, _) => capturedUser = u);

        var controller = NewUserController();
        var value = new UserRegistration { Email = "newbie@example.com", Username = "newbie", Password = "TestPass123!", TenantName = "default" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(capturedUser!.Enabled);
    }


    [Fact]
    public async Task UserLogin_CorrectCredentials_ReturnsOkWithToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", TenantID = 0, EmailVerified = true, Enabled = true });
        _repo.Setup(r => r.UserSecretGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });
        _repo.Setup(r => r.UserRoleNamesGetAsync(5)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.RefreshTokenAddAsync(5, It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(1);

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "alice@example.com", Password = password });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.Equal("alice@example.com", login.Email);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        // Legacy "user" alias first (first-role-claim readers expect it), then the real role.
        Assert.Equal(new[] { "user", RoleNames.TenantReader }, JwtTokenProvider.ValidateToken(login.Token!));
    }

    [Fact]
    public async Task UserLogin_WrongPassword_Returns401()
    {
        string salt = AuthenticationProvider.GetSalt();
        string hashForRealPassword = AuthenticationProvider.GetHash("the-real-password", salt);

        _repo.Setup(r => r.UserGetAsync(null, "bob@example.com", null))
             .ReturnsAsync(new User { IDUser = 6, Email = "bob@example.com", TenantID = 0 });
        _repo.Setup(r => r.UserSecretGetAsync(null, "bob@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hashForRealPassword, PwdSalt = salt });

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "bob@example.com", Password = "wrong-password" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task UserLogin_NoSuchUser_Returns401_NotA503()
    {
        _repo.Setup(r => r.UserGetAsync(null, "ghost@example.com", null)).ReturnsAsync((User?)null);
        _repo.Setup(r => r.UserSecretGetAsync(null, "ghost@example.com", null)).ReturnsAsync((UserSecret?)null);

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "ghost@example.com", Password = "whatever" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        Assert.Equal("Wrong username or password", obj.Value);
    }


    /// Enabled is the only login gate - an admin-enabled account with EmailVerified still false must still sign in.
    [Fact]
    public async Task UserLogin_EmailNotVerified_ButEnabled_StillSucceeds()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new User { IDUser = 7, Email = "pending@example.com", TenantID = 0, EmailVerified = false, Enabled = true });
        _repo.Setup(r => r.UserSecretGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });
        _repo.Setup(r => r.UserRoleNamesGetAsync(7)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.RefreshTokenAddAsync(7, It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(1);

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "pending@example.com", Password = password });

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UserLogin_EmailVerified_ButNotEnabled_Returns403_NotAToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "waiting@example.com", null))
             .ReturnsAsync(new User { IDUser = 8, Email = "waiting@example.com", TenantID = 1, EmailVerified = true, Enabled = false });
        _repo.Setup(r => r.UserSecretGetAsync(null, "waiting@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "waiting@example.com", Password = password });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task ForceChangePassword_NewPasswordTooShort_Returns400_NeverLooksUpUser()
    {
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { PasswordMinLength = 8 });

        var controller = NewUserController();
        var result = await controller.ForceChangePassword(new UserForceChangePassword
        {
            Login = "imported@example.com",
            OldPassword = "old-imported-pw",
            NewPassword = "short1",
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        // Strict mock: an un-set-up UserGetAsync/UserSecretGetAsync call would throw, proving the policy check ran first.
    }


    [Fact]
    public async Task BootstrapPending_DelegatesToRepo()
    {
        _repo.Setup(r => r.BootstrapAdminPendingAsync()).ReturnsAsync(true);

        var controller = NewUserController();
        var result = await controller.BootstrapPending();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)ok.Value!);
    }

    [Fact]
    public async Task BootstrapSetPassword_PendingAdminExists_HashesAndReturnsOk()
    {
        UserSecret? captured = null;
        _repo.Setup(r => r.BootstrapAdminSetPasswordAsync(It.IsAny<UserSecret>(), "right-secret"))
             .Callback<UserSecret, string>((s, _) => captured = s)
             .ReturnsAsync(true);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        var result = await controller.BootstrapSetPassword(new BootstrapAdminSetPassword { NewPassword = "hunter2!", SetupSecret = "right-secret" });

        Assert.IsType<OkResult>(result);
        // The plaintext must never reach the repo - only a freshly generated hash+salt.
        Assert.NotNull(captured);
        Assert.NotEqual("hunter2!", captured!.PwdHash);
        Assert.Equal(captured.PwdHash, AuthenticationProvider.GetHash("hunter2!", captured.PwdSalt!));
    }

    [Fact]
    public async Task BootstrapSetPassword_NoPendingAdmin_Returns403()
    {
        _repo.Setup(r => r.BootstrapAdminSetPasswordAsync(It.IsAny<UserSecret>(), It.IsAny<string>())).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        var result = await controller.BootstrapSetPassword(new BootstrapAdminSetPassword { NewPassword = "hunter2!", SetupSecret = "wrong-secret" });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
    }


    private static string HashRefreshToken(string plaintext) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext)));

    [Fact]
    public async Task RefreshToken_ValidToken_RotatesAndReturnsNewAccessToken()
    {
        const string presented = "stale-but-valid-refresh-token";
        string hash = HashRefreshToken(presented);

        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 5, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null });
        _repo.Setup(r => r.UserGetAsync(5, null, null))
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", TenantID = 0, EmailVerified = true, Enabled = true });
        _repo.Setup(r => r.UserRoleNamesGetAsync(5)).ReturnsAsync(new List<string> { RoleNames.GlobalAdmin });
        _repo.Setup(r => r.RefreshTokenRotateAsync(5, hash, It.IsAny<string>(), It.IsAny<DateTime>()))
             .ReturnsAsync(true);

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = presented });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.NotEqual(presented, login.RefreshToken); // rotated, not reissued
        Assert.Equal(new[] { RoleNames.LegacyAdmin, RoleNames.GlobalAdmin }, JwtTokenProvider.ValidateToken(login.Token!));
        _repo.Verify(r => r.RefreshTokenRotateAsync(5, hash, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_UserHasNoRoles_Returns500()
    {
        const string presented = "stale-but-valid-refresh-token-2";
        string hash = HashRefreshToken(presented);

        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 6, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null });
        _repo.Setup(r => r.UserGetAsync(6, null, null))
             .ReturnsAsync(new User { IDUser = 6, Email = "bob@example.com", TenantID = 0, EmailVerified = true, Enabled = true });
        _repo.Setup(r => r.UserRoleNamesGetAsync(6)).ReturnsAsync(new List<string>());

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = presented });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, obj.StatusCode);
    }

    /// RefreshTokenRotateAsync returning false (lost the rotate race) must be treated the same as detected reuse - all sessions revoked, 401.
    [Fact]
    public async Task RefreshToken_LosesAtomicRotateRace_RevokesAllSessionsAndReturns401()
    {
        const string presented = "raced-refresh-token";
        string hash = HashRefreshToken(presented);

        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 7, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null });
        _repo.Setup(r => r.UserGetAsync(7, null, null))
             .ReturnsAsync(new User { IDUser = 7, Email = "raced@example.com", TenantID = 0, EmailVerified = true, Enabled = true });
        _repo.Setup(r => r.UserRoleNamesGetAsync(7)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.RefreshTokenRotateAsync(7, hash, It.IsAny<string>(), It.IsAny<DateTime>()))
             .ReturnsAsync(false);
        _repo.Setup(r => r.RefreshTokenRevokeAllForUserAsync(7)).Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = presented });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        _repo.Verify(r => r.RefreshTokenRevokeAllForUserAsync(7), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_UnknownToken_Returns401()
    {
        _repo.Setup(r => r.RefreshTokenGetAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenInfo?)null);

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = "never-issued" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_AlreadyRotatedToken_DetectsReuseAndRevokesAllSessions()
    {
        const string stolen = "already-used-token";
        string hash = HashRefreshToken(stolen);

        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 9, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = DateTime.UtcNow.AddMinutes(-5) });
        _repo.Setup(r => r.RefreshTokenRevokeAllForUserAsync(9)).Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = stolen });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        _repo.Verify(r => r.RefreshTokenRevokeAllForUserAsync(9), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_Returns401_DoesNotRotate()
    {
        string hash = HashRefreshToken("expired-token");
        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 3, ExpiresAt = DateTime.UtcNow.AddMinutes(-1), RevokedAt = null });

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = "expired-token" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        // MockBehavior.Strict: an un-set-up RefreshTokenRotateAsync/RevokeAllForUserAsync call would throw, proving neither was called.
    }

    [Fact]
    public async Task RefreshToken_ValidToken_ButUserDisabledSinceIssue_Returns403_DoesNotRotate()
    {
        // A refresh token issued before an admin disabled the account must not keep minting fresh access tokens.
        string hash = HashRefreshToken("still-technically-valid");
        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 11, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null });
        _repo.Setup(r => r.UserGetAsync(11, null, null))
             .ReturnsAsync(new User { IDUser = 11, Email = "disabled@example.com", TenantID = 1, EmailVerified = true, Enabled = false });

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = "still-technically-valid" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        // MockBehavior.Strict: an un-set-up RefreshTokenRotateAsync call would throw.
    }

    [Fact]
    public async Task RevokeRefreshToken_UnknownToken_StillReturnsOk()
    {
        _repo.Setup(r => r.RefreshTokenRevokeAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.RevokeRefreshToken(new RefreshTokenRequest { RefreshToken = "whatever" });

        Assert.IsType<OkResult>(result);
    }


    [Fact]
    public async Task UserGet_DifferentTenant_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserGet(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task UserGet_UnknownId_Returns404()
    {
        _repo.Setup(r => r.UserGetAsync(999, null, null)).ReturnsAsync((User?)null);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserGet(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UsersGet_UsesCallerTenant_NotHardcodedDefault()
    {
        // Strict mock: a hard-coded 0 instead of the caller's claim wouldn't match this setup and would throw.
        _repo.Setup(r => r.UsersGetAsync(7)).ReturnsAsync(new List<User> { new() { IDUser = 1, TenantID = 7 } });

        var controller = NewUserController();
        SetCaller(controller, "admin", 7);
        var result = await controller.UsersGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IList<User>>(ok.Value));
    }

    [Fact]
    public async Task UserAdd_IgnoresPayloadTenantID_UsesCallersTenant()
    {
        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "x@test.local", null)).ReturnsAsync(new User { IDUser = 99, Email = "x@test.local" });
        _repo.Setup(r => r.UserRolesSetAsync(99, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { TenantID = 999, Email = "x@test.local", Username = "x", Password = "TestPass123!", Enabled = true };
        var result = await controller.UserAdd(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(24, capturedUser!.TenantID); // not 999 from the payload
    }

    [Fact]
    public async Task UserAdd_AdminRequestsTenantAdmin_Applied()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "boss@test.local", null)).ReturnsAsync(new User { IDUser = 100, Email = "boss@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(100, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 24); // NOT tenant 0 - a regular Tenant admin, not Global admin
        var value = new UserAdd { Email = "boss@test.local", Username = "boss", Password = "TestPass123!", RoleNames = new() { RoleNames.TenantAdmin }, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.TenantAdmin }, seededRoles);
    }

    [Fact]
    public async Task UserAdd_GlobalAdminRequestsGlobalAdmin_Applied()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "boss@test.local", null)).ReturnsAsync(new User { IDUser = 101, Email = "boss@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(101, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 0); // Global admin
        var value = new UserAdd { Email = "boss@test.local", Username = "boss", Password = "TestPass123!", RoleNames = new() { RoleNames.GlobalAdmin }, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.GlobalAdmin }, seededRoles);
    }

    [Fact]
    public async Task UserAdd_AdminRequestsNoRoles_DefaultsToTenantReader()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "newbie@test.local", null)).ReturnsAsync(new User { IDUser = 102, Email = "newbie@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(102, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { Email = "newbie@test.local", Username = "newbie", Password = "TestPass123!", Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.TenantReader }, seededRoles);
    }

    /// A non-admin UserManager forging a TenantAdmin RoleNames list must have it silently dropped in favor of the safe default.
    [Fact]
    public async Task UserAdd_NonAdminCaller_RequestedRolesIgnored_DefaultsToTenantReader()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "sneaky@test.local", null)).ReturnsAsync(new User { IDUser = 103, Email = "sneaky@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(103, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCallerRoles(controller, 24, "user", RoleNames.TenantReader, RoleNames.TenantUser); // UserManagers-eligible, not an admin
        var value = new UserAdd { Email = "sneaky@test.local", Username = "sneaky", Password = "TestPass123!", RoleNames = new() { RoleNames.TenantAdmin }, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.TenantReader }, seededRoles);
    }

    /// A Tenant admin may only grant Tenant-scoped roles - requesting a Global role must 403.
    [Fact]
    public async Task UserAdd_TenantAdminRequestsGlobalRole_Returns403()
    {
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 24); // Tenant admin, not Global
        var value = new UserAdd { Email = "x@test.local", Username = "x", Password = "TestPass123!", RoleNames = new() { RoleNames.GlobalAdmin }, Enabled = true };

        var result = await controller.UserAdd(value);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: UserAddAsync was never set up - a disallowed role must reject before writing anything.
    }

    [Fact]
    public async Task UserDelete_DifferentTenant_Returns403AndDoesNotCallDelete()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.Delete(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserDeleteAsync(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task UserUpdate_DifferentTenant_Returns403_EvenForEmailOnlyChange()
    {
        // Regression guard: the tenant check must fire for any change, not only Enabled.
        _repo.Setup(r => r.UserGetAsync(50, null, null))
             .ReturnsAsync(new User { IDUser = 50, TenantID = 99, Email = "target@test.local" });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, Email = "hijacked@evil.local" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }


    [Fact]
    public async Task Activate_ValidToken_TenantZero_EnablesDirectly_ReportsCanSignIn()
    {
        const string plaintext = "the-activation-token";
        string hash = HashRefreshToken(plaintext);
        _repo.Setup(r => r.UserActivateAsync(hash))
             .ReturnsAsync(new User { IDUser = 1, Email = "a@example.com", TenantID = 0, Enabled = false });
        User? updatedUser = null;
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>()))
             .Callback<User>(u => updatedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.Activate(plaintext);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("now sign in", (string)ok.Value!);
        Assert.True(updatedUser!.Enabled);
        // Strict mock: no UserRoleNamesGetAsync/TenantAdminsGetAsync/DispatchAsync setup needed - tenant 0 short-circuits before any of them run.
    }

    [Fact]
    public async Task Activate_TenantsOwnCreator_EnablesDirectly_NoApprovalNeeded()
    {
        const string plaintext = "the-activation-token";
        string hash = HashRefreshToken(plaintext);
        _repo.Setup(r => r.UserActivateAsync(hash))
             .ReturnsAsync(new User { IDUser = 9, Email = "owner@acme.local", TenantID = 42, Enabled = false });
        _repo.Setup(r => r.UserRoleNamesGetAsync(9)).ReturnsAsync(new List<string> { RoleNames.TenantAdmin });
        User? updatedUser = null;
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>()))
             .Callback<User>(u => updatedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.Activate(plaintext);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("now sign in", (string)ok.Value!);
        Assert.True(updatedUser!.Enabled);
        // Strict mock: no TenantAdminsGetAsync/DispatchAsync setup needed - holding TenantAdmin already skips the approval branch.
    }

    [Fact]
    public async Task Activate_ValidToken_ExistingNonZeroTenant_StillDisabled_NotifiesTenantAdmins()
    {
        const string plaintext = "the-activation-token";
        string hash = HashRefreshToken(plaintext);
        var activatedUser = new User { IDUser = 2, Email = "member@acme.local", Username = "member", TenantID = 42, Enabled = false };
        _repo.Setup(r => r.UserActivateAsync(hash)).ReturnsAsync(activatedUser);
        _repo.Setup(r => r.UserRoleNamesGetAsync(2)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.TenantAdminsGetAsync(42))
             .ReturnsAsync(new List<User> { new() { Email = "admin@acme.local" } });

        var controller = NewUserController();
        var result = await controller.Activate(plaintext);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("administrator has been notified", (string)ok.Value!);
        await RunOneQueuedJobAsync();
        _notifications.Verify(n => n.DispatchAsync(
            It.Is<Notification>(msg => msg.Recipient.Email == "admin@acme.local"),
            It.IsAny<CancellationToken>()), Times.Once);
        // Strict mock: no UserUpdateAsync setup needed - the approval branch never enables the account itself.
    }

    [Fact]
    public async Task Activate_UnknownOrExpiredToken_Returns400()
    {
        _repo.Setup(r => r.UserActivateAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var controller = NewUserController();
        var result = await controller.Activate("whatever");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task Activate_MissingToken_Returns400_DoesNotHitRepo()
    {
        // Strict mock: an un-set-up UserActivateAsync call would throw, proving the controller short-circuited before touching the repo.
        var controller = NewUserController();
        var result = await controller.Activate(null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResendActivation_AlreadyVerified_SendsNothing_ButStillReturnsGenericOk()
    {
        _repo.Setup(r => r.UserGetAsync(null, "verified@example.com", null))
             .ReturnsAsync(new User { IDUser = 3, Email = "verified@example.com", EmailVerified = true });
        _repo.Setup(r => r.UserSecretGetAsync(null, "verified@example.com", null)).ReturnsAsync((UserSecret?)null);

        var controller = NewUserController();
        var result = await controller.ResendActivation(new ResendActivationRequest { Login = "verified@example.com" });

        Assert.IsType<OkObjectResult>(result);
        // Strict mock: an un-set-up ServerConfigGetAsync/UserIssueActivationTokenAsync call would throw, proving EmailVerified==true short-circuited before either ran.
    }

    [Fact]
    public async Task ResendActivation_UnknownLogin_SendsNothing_ButStillReturnsGenericOk()
    {
        _repo.Setup(r => r.UserGetAsync(null, "ghost@example.com", null)).ReturnsAsync((User?)null);
        _repo.Setup(r => r.UserSecretGetAsync(null, "ghost@example.com", null)).ReturnsAsync((UserSecret?)null);

        var controller = NewUserController();
        var result = await controller.ResendActivation(new ResendActivationRequest { Login = "ghost@example.com" });

        Assert.IsType<OkObjectResult>(result);
        // Same enumeration-safety guarantee as the "already verified" case above - identical response.
    }

    [Fact]
    public async Task ResendActivation_UnverifiedAndOffCooldown_IssuesNewTokenAndEmails()
    {
        _repo.Setup(r => r.UserGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new User { IDUser = 4, Email = "pending@example.com", EmailVerified = false });
        _repo.Setup(r => r.UserSecretGetAsync(null, "pending@example.com", null)).ReturnsAsync((UserSecret?)null);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { ActivationResendCooldownMinutes = 10 });
        _repo.Setup(r => r.UserIssueActivationTokenAsync(4, It.IsAny<string>(), It.IsAny<DateTime>(), 10)).ReturnsAsync(true);

        var controller = NewUserController();
        var result = await controller.ResendActivation(new ResendActivationRequest { Login = "pending@example.com" });

        Assert.IsType<OkObjectResult>(result);
        await RunOneQueuedJobAsync();
        _notifications.Verify(n => n.DispatchAsync(
            It.Is<Notification>(msg => msg.Recipient.Email == "pending@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendActivation_StillInCooldown_SendsNoEmail()
    {
        _repo.Setup(r => r.UserGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new User { IDUser = 4, Email = "pending@example.com", EmailVerified = false });
        _repo.Setup(r => r.UserSecretGetAsync(null, "pending@example.com", null)).ReturnsAsync((UserSecret?)null);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { ActivationResendCooldownMinutes = 10 });
        _repo.Setup(r => r.UserIssueActivationTokenAsync(4, It.IsAny<string>(), It.IsAny<DateTime>(), 10)).ReturnsAsync(false);

        var controller = NewUserController();
        var result = await controller.ResendActivation(new ResendActivationRequest { Login = "pending@example.com" });

        Assert.IsType<OkObjectResult>(result);
        AssertNoJobWasQueued();
    }


    [Fact]
    public async Task UsersGet_GlobalAdmin_ReturnsEveryTenant_NotJustItsOwn()
    {
        _repo.Setup(r => r.UsersGetAllAsync())
             .ReturnsAsync(new List<User> { new() { IDUser = 1, TenantID = 0 }, new() { IDUser = 2, TenantID = 7 } });

        var controller = NewUserController();
        SetCaller(controller, "admin", 0); // TenantID==0 admin = global admin
        var result = await controller.UsersGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsAssignableFrom<IList<User>>(ok.Value).Count);
        // Strict mock: an un-set-up UsersGetAsync(0) call would throw, proving the "all tenants" path was taken instead of the normal tenant-scoped one.
    }

    [Fact]
    public async Task UserGet_GlobalAdmin_DifferentTenant_BypassesTheTenantCheck()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.UserGet(50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(50, ((User)ok.Value!).IDUser);
    }

    [Fact]
    public async Task UserUpdate_GlobalAdmin_CanReassignTenantID()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99, Email = "x@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        User? capturedUser = null;
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>()))
             .Callback<User>(u => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, TenantID = 7 });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(7, capturedUser!.TenantID);
    }

    [Fact]
    public async Task UserUpdate_NonGlobalAdmin_CannotReassignTenantID()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        User? capturedUser = null;
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>()))
             .Callback<User>(u => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1); // a regular (non-global) tenant admin
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, TenantID = 7 });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, capturedUser!.TenantID); // payload's TenantID=7 silently ignored
    }

    [Fact]
    public async Task UserDelete_GlobalAdmin_DifferentTenant_BypassesTheTenantCheck()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserDeleteAsync(50)).ReturnsAsync(true);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.Delete(50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("User deleted", ok.Value);
    }


    [Fact]
    public async Task UserRolesGet_DifferentTenant_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserRolesGet(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task UserRolesGet_GlobalAdmin_DifferentTenant_ReturnsRoles()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.UserRolesGet(50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new[] { RoleNames.TenantReader }, ok.Value);
    }

    [Fact]
    public async Task UserRolesSet_TenantAdmin_CannotAssignGlobalRole_Returns403AndDoesNotWrite()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1 });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1); // regular Tenant admin, not Global admin
        var result = await controller.UserRolesSet(new UserRolesUpdate { IDUser = 50, RoleNames = new List<string> { RoleNames.GlobalAdmin } });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserRolesSetAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task UserRolesSet_TenantAdmin_CanComposeReaderPlusDeviceGrant()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1 });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        List<string>? written = null;
        _repo.Setup(r => r.UserRolesSetAsync(50, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => written = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var value = new UserRolesUpdate { IDUser = 50, RoleNames = new List<string> { RoleNames.TenantReader, RoleNames.TenantDevice } };
        var result = await controller.UserRolesSet(value);

        Assert.IsType<OkResult>(result);
        Assert.Equal(new[] { RoleNames.TenantReader, RoleNames.TenantDevice }, written);
    }

    [Fact]
    public async Task UserRolesSet_GlobalAdmin_CanAssignGlobalRoleToAnyTenant()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string>());
        _repo.Setup(r => r.UserRolesSetAsync(50, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.UserRolesSet(new UserRolesUpdate { IDUser = 50, RoleNames = new List<string> { RoleNames.GlobalReader } });

        Assert.IsType<OkResult>(result);
    }


    private ServerConfigApiController NewServerConfigController() => new(_repo.Object, _cache.Object);
    private SensorDataController NewSensorDataController() => new(_repo.Object, _cache.Object);

    [Fact]
    public async Task DeviceUpdate_TenantDevice_OwnTenant_Succeeds()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });
        _repo.Setup(r => r.DeviceUpdateAsync(It.IsAny<Device>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 8 });

        Assert.True(result.Value);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Once);
    }

    [Fact]
    public async Task DeviceUpdate_DefaultTenantDevice_CallerOwnsDefaultTenant_Succeeds()
    {
        // TenantID=0 is a real default tenant, not a "no tenant" sentinel - its own admin must be able to manage devices there.
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 0 });
        _repo.Setup(r => r.DeviceUpdateAsync(It.IsAny<Device>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 0, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 8 });

        Assert.True(result.Value);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Once);
    }

    [Fact]
    public async Task DeviceUpdate_TenantDevice_ForeignTenant_Returns403()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 8 });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Never);
    }

    [Fact]
    public async Task DeviceUpdate_GlobalDevice_CrossesTenants()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });
        _repo.Setup(r => r.DeviceUpdateAsync(It.IsAny<Device>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.GlobalDevice);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 8 });

        Assert.True(result.Value);
    }

    [Fact]
    public async Task DeviceUpdate_GlobalReader_ForeignTenant_Returns403_ReadNeverImpliesWrite()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.GlobalReader);
        var result = await controller.DeviceUpdate(new DeviceDto { IDDevice = 8 });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }


    [Fact]
    public async Task DeviceConfigControllerUpdate_RelayMappingOnly_Persists()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 0 });
        _repo.Setup(r => r.DeviceConfigControllerUpdateAsync(8, It.IsAny<DeviceConfigController>())).ReturnsAsync((string?)null);

        var controller = NewDeviceController();
        SetCaller(controller, "admin", 0);

        var result = await controller.DeviceConfigControllerUpdate(new DeviceUpdate
        {
            Device = new DeviceDto { IDDevice = 8 },
            Controller = new DeviceConfigController { RelayEnabled = true, Relays = [new DeviceRelaySlot { Slot = 1, RelayFunction = 16 }] },
        });

        Assert.True(result.Value);
        _repo.Verify(r => r.DeviceConfigControllerUpdateAsync(8, It.IsAny<DeviceConfigController>()), Times.Once);
    }


    [Fact]
    public async Task TenantUpdate_UnknownScheduleTimeZone_Returns400_AndNeverWrites()
    {
        var controller = NewTenantController();
        SetCaller(controller, "admin", 0);

        var result = await controller.TenantUpdate(new Tenant { IDTenant = 1, TenantName = "t1", ScheduleTimeZone = "Not/AZone" });

        Assert.IsType<BadRequestObjectResult>(result);
        // MockBehavior.Strict: TenantUpdateAsync has no setup, proving the bad id was rejected before any write.
    }

    [Fact]
    public async Task TenantUpdate_ValidScheduleTimeZone_NormalizesToIana_AndPersists()
    {
        Tenant? saved = null;
        _repo.Setup(r => r.TenantUpdateAsync(It.IsAny<Tenant>()))
             .Callback<Tenant>(t => saved = t)
             .Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCaller(controller, "admin", 0);

        var result = await controller.TenantUpdate(new Tenant { IDTenant = 1, TenantName = "t1", ScheduleTimeZone = "Europe/Zagreb" });

        Assert.IsType<OkResult>(result);
        Assert.Equal("Europe/Zagreb", saved!.ScheduleTimeZone);
    }

    [Fact]
    public async Task TenantUpdate_BlankScheduleTimeZone_ClearsToNull()
    {
        // Blank is a valid "not configured" state (see api.Models.Tenant) - must not be rejected like an actually-invalid value.
        Tenant? saved = null;
        _repo.Setup(r => r.TenantUpdateAsync(It.IsAny<Tenant>()))
             .Callback<Tenant>(t => saved = t)
             .Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCaller(controller, "admin", 0);

        var result = await controller.TenantUpdate(new Tenant { IDTenant = 1, TenantName = "t1", ScheduleTimeZone = "   " });

        Assert.IsType<OkResult>(result);
        Assert.Null(saved!.ScheduleTimeZone);
    }

    [Fact]
    public async Task EmergencyStopActivate_OwnTenant_Succeeds_AndAudits()
    {
        _repo.Setup(r => r.TenantEmergencyStopSetAsync(5, true)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCallerRoles(controller, 5, RoleNames.TenantDevice);

        var result = await controller.EmergencyStopActivate();

        Assert.IsType<OkResult>(result);
        _repo.Verify(r => r.TenantEmergencyStopSetAsync(5, true), Times.Once);
    }

    [Fact]
    public async Task EmergencyStopActivate_AnotherTenant_Returns403_AndNeverWrites()
    {
        var controller = NewTenantController();
        SetCallerRoles(controller, 5, RoleNames.TenantDevice);

        var result = await controller.EmergencyStopActivate(idTenant: 6);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        // MockBehavior.Strict: TenantEmergencyStopSetAsync has no setup, proving cross-tenant was rejected before any write.
    }

    [Fact]
    public async Task EmergencyStopActivate_GlobalAdmin_CanTargetAnyTenant()
    {
        _repo.Setup(r => r.TenantEmergencyStopSetAsync(6, true)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCallerRoles(controller, 0, RoleNames.GlobalAdmin);

        var result = await controller.EmergencyStopActivate(idTenant: 6);

        Assert.IsType<OkResult>(result);
        _repo.Verify(r => r.TenantEmergencyStopSetAsync(6, true), Times.Once);
    }

    [Fact]
    public async Task EmergencyStopClear_OwnTenant_Succeeds()
    {
        _repo.Setup(r => r.TenantEmergencyStopSetAsync(5, false)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCallerRoles(controller, 5, RoleNames.TenantAdmin);

        var result = await controller.EmergencyStopClear();

        Assert.IsType<OkResult>(result);
        _repo.Verify(r => r.TenantEmergencyStopSetAsync(5, false), Times.Once);
    }

    [Fact]
    public async Task EmergencyStopStatus_ReturnsCurrentTenantFlag()
    {
        _repo.Setup(r => r.TenantGetByIdAsync(5)).ReturnsAsync(new Tenant { IDTenant = 5, EmergencyStopActive = true });

        var controller = NewTenantController();
        SetCallerRoles(controller, 5, RoleNames.TenantDevice);

        var result = await controller.EmergencyStopStatus();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((bool)ok.Value!);
    }


    [Fact]
    public async Task ServerConfigUpdate_WaterPumpMaxRunSecondsNegative_Returns400_AndNeverWrites()
    {
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { WaterPumpMaxRunSeconds = -1 });

        Assert.IsType<BadRequestObjectResult>(result);
        // MockBehavior.Strict: ServerConfigUpdateAsync has no setup, proving the bad value was rejected before any write.
    }

    [Fact]
    public async Task ServerConfigUpdate_WaterPumpCooldownSecondsTooLarge_Returns400()
    {
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { WaterPumpCooldownSeconds = 86401 });

        Assert.IsType<BadRequestObjectResult>(result);
    }


    [Fact]
    public async Task ServerConfigUpdate_SensorDataRetentionDaysNegative_Returns400_AndNeverWrites()
    {
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { SensorDataRetentionDays = -1 });

        Assert.IsType<BadRequestObjectResult>(result);
        // MockBehavior.Strict: ServerConfigUpdateAsync has no setup, proving the bad value was rejected before any write.
    }

    [Fact]
    public async Task ServerConfigUpdate_SensorDataRetentionDaysNullOrPositive_Persists()
    {
        ServerConfig? saved = null;
        _repo.Setup(r => r.ServerConfigUpdateAsync(It.IsAny<ServerConfig>()))
             .Callback<ServerConfig>(c => saved = c)
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { SensorDataRetentionDays = 365 });

        Assert.IsType<OkResult>(result);
        Assert.Equal(365, saved!.SensorDataRetentionDays);
    }

    [Fact]
    public async Task ServerConfigUpdate_ProblemEventExpiryHoursNotInFixedSet_Returns400_AndNeverWrites()
    {
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { ProblemEventExpiryHours = 3 });

        Assert.IsType<BadRequestObjectResult>(result);
        // MockBehavior.Strict: ServerConfigUpdateAsync has no setup, proving the bad value was rejected before any write.
    }

    [Fact]
    public async Task ServerConfigUpdate_ProblemEventExpiryHoursInFixedSet_Persists()
    {
        ServerConfig? saved = null;
        _repo.Setup(r => r.ServerConfigUpdateAsync(It.IsAny<ServerConfig>()))
             .Callback<ServerConfig>(c => saved = c)
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0);

        var result = await controller.Update(new ServerConfig { ProblemEventExpiryHours = 6 });

        Assert.IsType<OkResult>(result);
        Assert.Equal(6, saved!.ProblemEventExpiryHours);
    }

    [Fact]
    public async Task DevicesGet_GlobalReader_SeesEveryTenant()
    {
        // Strict mock: an un-set-up DevicesGetAsync(3) call would throw, proving the all-tenants path was taken.
        _repo.Setup(r => r.DevicesGetAllAsync()).ReturnsAsync(new List<Device>
        {
            new() { IDDevice = 1, TenantID = 0 }, new() { IDDevice = 2, TenantID = 7 },
        });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 3, "user", RoleNames.GlobalReader);
        var result = await controller.DevicesGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<DeviceDto>>(ok.Value).Count());
    }

    [Fact]
    public async Task DevicesGet_TenantReader_ScopedToOwnTenant()
    {
        _repo.Setup(r => r.DevicesGetAsync(3)).ReturnsAsync(new List<Device> { new() { IDDevice = 1, TenantID = 3 } });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 3, "user", RoleNames.TenantReader);
        var result = await controller.DevicesGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<DeviceDto>>(ok.Value));
    }

    [Fact]
    public async Task DeviceGet_GlobalReader_UsesUnfilteredLookup()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(42)).ReturnsAsync(new Device { IDDevice = 42, TenantID = 9 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.GlobalReader);
        var result = await controller.DeviceGet(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(42, ((DeviceDto)ok.Value!).IDDevice);
    }

    [Fact]
    public async Task DeviceDelete_GlobalAdmin_ForeignTenant_DeletesWithTheDevicesOwnTenant()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 99 });
        _repo.Setup(r => r.DeviceDeleteAsync(7, 99)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 0, "admin", RoleNames.GlobalAdmin);
        var result = await controller.DeviceDelete(7);

        Assert.True(result.Value);
        // Strict mock proves the delete used TenantID 99 (the device's), not 0 (the caller's).
        _repo.Verify(r => r.DeviceDeleteAsync(7, 99), Times.Once);
    }

    [Fact]
    public async Task DeviceEventsGet_TenantReader_OwnTenant_Ok()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(5)).ReturnsAsync(new Device { IDDevice = 5, TenantID = 2 });
        _repo.Setup(r => r.EventDeviceGetAsync(5, 2, 100)).ReturnsAsync(new List<DeviceEvent>());

        var controller = NewDeviceController();
        SetCallerRoles(controller, 2, "user", RoleNames.TenantReader);
        var result = await controller.DeviceEventsGet(5);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeviceEventsGet_TenantReader_ForeignTenant_Returns403()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(5)).ReturnsAsync(new Device { IDDevice = 5, TenantID = 99 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 2, "user", RoleNames.TenantReader);
        var result = await controller.DeviceEventsGet(5);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task DeviceEventAcknowledge_TenantDevice_PassesOwnTenantId_NotNull()
    {
        _repo.Setup(r => r.EventDeviceAcknowledgeAsync(9, 2)).ReturnsAsync(true);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 2, "user", RoleNames.TenantDevice);
        var result = await controller.DeviceEventAcknowledge(9);

        Assert.True(result.Value);
        // Strict mock: a call with tenantID null (the global-caller path) would not match this setup.
        _repo.Verify(r => r.EventDeviceAcknowledgeAsync(9, 2), Times.Once);
    }

    [Fact]
    public async Task DeviceEventAcknowledge_GlobalDevice_PassesNullTenantId()
    {
        _repo.Setup(r => r.EventDeviceAcknowledgeAsync(9, null)).ReturnsAsync(true);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 0, "admin", RoleNames.GlobalDevice);
        var result = await controller.DeviceEventAcknowledge(9);

        Assert.True(result.Value);
        _repo.Verify(r => r.EventDeviceAcknowledgeAsync(9, null), Times.Once);
    }

    [Fact]
    public async Task DeviceEventAcknowledge_NoMatchingRow_Returns404()
    {
        _repo.Setup(r => r.EventDeviceAcknowledgeAsync(9, 2)).ReturnsAsync(false);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 2, "user", RoleNames.TenantDevice);
        var result = await controller.DeviceEventAcknowledge(9);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UsersGet_GlobalReader_SeesEveryTenant()
    {
        _repo.Setup(r => r.UsersGetAllAsync()).ReturnsAsync(new List<User> { new() { IDUser = 1, TenantID = 0 }, new() { IDUser = 2, TenantID = 7 } });

        var controller = NewUserController();
        SetCallerRoles(controller, 3, "user", RoleNames.GlobalReader);
        var result = await controller.UsersGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsAssignableFrom<IList<User>>(ok.Value).Count);
    }

    [Fact]
    public async Task UserUpdate_TenantUser_OwnTenant_Succeeds()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantUser);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task UserUpdate_AdminChangesRoles_Applied()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(50, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig());

        var controller = NewUserController();
        SetCaller(controller, "admin", 1); // Tenant admin
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, RoleNames = new() { RoleNames.TenantUser, RoleNames.TenantDevice } });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(new[] { RoleNames.TenantUser, RoleNames.TenantDevice }, seededRoles);
    }

    /// Same guard as UserAdd, on the Update path - strict mock: UserRolesSetAsync was never set up.
    [Fact]
    public async Task UserUpdate_NonAdminCaller_RequestedRolesIgnored()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantUser); // UserManagers-eligible, not an admin
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, RoleNames = new() { RoleNames.TenantAdmin } });

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UserUpdate_TenantAdminRequestsGlobalRole_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1); // Tenant admin, not Global
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, RoleNames = new() { RoleNames.GlobalDevice } });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task UserUpdate_TenantDevice_Returns403_DeviceGrantNeverImpliesUserManagement()
    {
        // Defence in depth: even if the attribute somehow let a device-only grant through, the inline CallerManagesUsers check must still refuse.
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task UserUpdate_TenantUser_TargetIsTenantAdmin_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "boss@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantAdmin });

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantUser);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, Enabled = false });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task UserDelete_TenantUser_TargetIsTenantAdmin_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "boss@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantAdmin });

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantUser);
        var result = await controller.Delete(50);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        _repo.Verify(r => r.UserDeleteAsync(It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task UserUpdate_TenantAdmin_TargetIsAnotherTenantAdmin_Succeeds()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "peer-admin@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantAdmin });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1); // Tenant admin
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task UserUpdate_GlobalUser_TargetIsGlobalAdmin_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "super@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.GlobalAdmin });

        var controller = NewUserController();
        SetCallerRoles(controller, 0, "user", RoleNames.GlobalReader, RoleNames.GlobalUser);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, Enabled = false });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task UserUpdate_GlobalUser_TargetIsTenantAdmin_Succeeds()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "tenant-admin@test.local" });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantAdmin });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCallerRoles(controller, 0, "user", RoleNames.GlobalReader, RoleNames.GlobalUser);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task UserUpdate_EnabledTrueToFalse_RevokesTokens()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local", Enabled = true });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.RevokeUserTokensAsync(50)).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, Enabled = false });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.RevokeUserTokensAsync(50), Times.Once);
    }

    [Fact]
    public async Task UserUpdate_EnabledStaysTrue_DoesNotRevokeTokens()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local", Enabled = true });
        _repo.Setup(r => r.UserRoleNamesGetAsync(50)).ReturnsAsync(new List<string> { RoleNames.TenantReader });
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        Assert.IsType<OkObjectResult>(result.Result);
        // Strict mock: an un-set-up RevokeUserTokensAsync/AuditLogAddAsync call would throw - proves neither ran when Enabled didn't change.
    }

    [Fact]
    public async Task ServerConfig_TenantAdmin_Returns403_ServerWideSettingsAreGlobalOnly()
    {
        // Strict mock: an un-set-up ServerConfigGetAsync call would throw, proving the request was refused before touching the repo.
        var controller = NewServerConfigController();
        SetCallerRoles(controller, 5, "admin", RoleNames.TenantAdmin);

        var get = await controller.Get();
        var put = await controller.Update(new ServerConfig());

        Assert.Equal(403, Assert.IsType<ObjectResult>(get.Result).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(put).StatusCode);
    }

    [Fact]
    public async Task ServerConfig_GlobalAdmin_CanReadAndWrite()
    {
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { IDServerConfig = 1 });
        _repo.Setup(r => r.ServerConfigUpdateAsync(It.IsAny<ServerConfig>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewServerConfigController();
        SetCallerRoles(controller, 0, "admin", RoleNames.GlobalAdmin);

        Assert.IsType<OkObjectResult>((await controller.Get()).Result);
        Assert.IsType<OkResult>(await controller.Update(new ServerConfig()));
    }

    [Fact]
    public async Task ServerConfig_LegacyOnlyTenant0Admin_StillAllowed_MigrationMissedFallback()
    {
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { IDServerConfig = 1 });

        var controller = NewServerConfigController();
        SetCaller(controller, "admin", 0); // single legacy claim, no #66 roles on the token

        Assert.IsType<OkObjectResult>((await controller.Get()).Result);
    }

    /// An out-of-range timeRange must 400 before reaching the repo - strict mock (SensorDataGetAsync never set up) proves it.
    [Fact]
    public async Task SensorDataGet_TimeRangeExceedsMaxForUnit_Returns400_NeverTouchesRepo()
    {
        var controller = NewSensorDataController();
        SetCallerRoles(controller, 4, "user", RoleNames.TenantReader);

        var result = await controller.Get(deviceID: 7, timeRange: 50, timeMDMY: 3); // 50 years - way past the 10-year cap

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task SensorDataGet_TimeRangeWithinMaxForUnit_Succeeds()
    {
        var controller = NewSensorDataController();
        SetCallerRoles(controller, 4, "user", RoleNames.TenantReader);
        _repo.Setup(r => r.SensorDataGetAsync(4, 7, 10, 3, 0)).ReturnsAsync("[]"); // 10 years - at the cap

        var result = await controller.Get(deviceID: 7, timeRange: 10, timeMDMY: 3);

        Assert.Equal("[]", Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    /// A batch over the cap must 400 before DeviceGetByApiIdAsync/SensorDataPushAsync run - strict mocks (neither set up) prove it.
    [Fact]
    public async Task SensorDataPost_BatchOverLimit_Returns400_NeverTouchesRepo()
    {
        var controller = NewSensorDataController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        var jsonArray = new JsonArray();
        for (int i = 0; i < 1001; i++)
        {
            jsonArray.Add(new JsonObject());
        }

        var result = await controller.Post(jsonArray);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task SensorDataPost_BatchAtLimit_Succeeds()
    {
        var controller = NewSensorDataController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Items[DeviceAuth.ApiIdItemKey] = "api-guid";

        _repo.Setup(r => r.DeviceGetByApiIdAsync("api-guid"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66 });
        _repo.Setup(r => r.SensorDataPushAsync(It.IsAny<JsonArray>(), 500, 3, It.IsAny<int?>(), It.IsAny<int?>()))
             .Returns(Task.CompletedTask);

        var jsonArray = new JsonArray();
        for (int i = 0; i < 1000; i++)
        {
            jsonArray.Add(new JsonObject());
        }

        var result = await controller.Post(jsonArray);

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.SensorDataPushAsync(It.IsAny<JsonArray>(), 500, 3, It.IsAny<int?>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task SensorDataDelete_TenantDevice_DeletesWithTheDevicesOwnTenant()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 4 });
        _repo.Setup(r => r.SensorDataDeleteAsync(4, 7, 0, 0)).Returns(Task.CompletedTask);

        var controller = NewSensorDataController();
        SetCallerRoles(controller, 4, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.Delete(7);

        Assert.IsType<OkResult>(result);
        _repo.Verify(r => r.SensorDataDeleteAsync(4, 7, 0, 0), Times.Once);
    }

    [Fact]
    public async Task SensorDataDelete_TenantUser_Returns403_UserGrantNeverImpliesDeviceManagement()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 4 });

        var controller = NewSensorDataController();
        SetCallerRoles(controller, 4, "user", RoleNames.TenantReader, RoleNames.TenantUser);
        var result = await controller.Delete(7);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private void StubEmptyTenantExport(int idTenant)
    {
        _repo.Setup(r => r.TenantGetByIdAsync(idTenant)).ReturnsAsync(new Tenant { IDTenant = idTenant, TenantName = "Acme" });
        _repo.Setup(r => r.UsersGetAsync(idTenant)).ReturnsAsync(new List<User>());
        _repo.Setup(r => r.DeviceUnitsGetAsync(idTenant)).ReturnsAsync(new List<DeviceUnit>());
        _repo.Setup(r => r.DevicesGetAsync(idTenant)).ReturnsAsync(new List<Device>());
    }

    [Fact]
    public async Task TenantExport_GlobalAdmin_WritesAuditLog()
    {
        StubEmptyTenantExport(7);
        AuditLogEntry? written = null;
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>()))
             .Callback<AuditLogEntry>(e => written = e)
             .Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCaller(controller, "admin", 0); // Global admin
        var result = await controller.Export(7);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(written);
        Assert.Equal("Tenant.Exported", written!.Action);
        Assert.Equal(7, written.TenantID);
    }

    [Fact]
    public async Task TenantExport_TenantAdmin_OwnTenant_WritesAuditLog()
    {
        StubEmptyTenantExport(7);
        _repo.Setup(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>())).Returns(Task.CompletedTask);

        var controller = NewTenantController();
        SetCallerRoles(controller, 7, "user", RoleNames.TenantReader, RoleNames.TenantAdmin); // Tenant admin of tenant 7
        var result = await controller.Export(7);

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.AuditLogAddAsync(It.IsAny<AuditLogEntry>()), Times.Once);
    }

    [Fact]
    public async Task TenantExport_TenantAdmin_DifferentTenant_Returns403_NoAuditLog()
    {
        var controller = NewTenantController();
        SetCallerRoles(controller, 7, "user", RoleNames.TenantReader, RoleNames.TenantAdmin); // Tenant admin of tenant 7, not 8
        var result = await controller.Export(8);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        // Strict mock: an un-set-up TenantGetByIdAsync/AuditLogAddAsync call would throw, proving the export never ran.
    }

    // Roadmap #294: IssueCommand's CreatedCommandIds had no way to check on afterward short of direct DB access.
    [Fact]
    public async Task GetCommand_OwnTenant_ReturnsStatus()
    {
        _repo.Setup(r => r.GetCommandByIdAsync(9)).ReturnsAsync(new DeviceCommand { IDDeviceCommand = 9, DeviceID = 8, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending });
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });

        var controller = NewDeviceCommandController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDevice);
        var result = await controller.GetCommand(9);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(CommandStatus.Pending, ((DeviceCommand)ok.Value!).Status);
    }

    [Fact]
    public async Task GetCommand_UnknownId_Returns404()
    {
        _repo.Setup(r => r.GetCommandByIdAsync(9)).ReturnsAsync((DeviceCommand?)null);

        var controller = NewDeviceCommandController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDevice);
        var result = await controller.GetCommand(9);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetCommand_ForeignTenant_Returns403()
    {
        _repo.Setup(r => r.GetCommandByIdAsync(9)).ReturnsAsync(new DeviceCommand { IDDeviceCommand = 9, DeviceID = 8, ActionType = CommandActionType.Reboot, Status = CommandStatus.Pending });
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceCommandController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantDevice);
        var result = await controller.GetCommand(9);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }
}

/// Regression guards for the Phase 2 role gates: asserts the [Authorize] attribute's role list rather than driving a request.
public class RoleGateAuthorizationTests
{
    private static string? RolesOn(Type controller, string method) =>
        controller.GetMethod(method)!.GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().SingleOrDefault()?.Roles;

    [Fact]
    public void SensorDataDelete_RequiresDeviceManagerRole()
    {
        Assert.Equal(RoleNames.DeviceManagers, RolesOn(typeof(SensorDataController), "Delete"));
    }

    [Fact]
    public void Delete_IsAnHttpDeleteEndpoint()
    {
        var del = typeof(SensorDataController).GetMethod("Delete")!;
        Assert.Contains(del.GetCustomAttributes(inherit: true), a => a is HttpDeleteAttribute);
    }

    [Fact]
    public void DeviceWrites_RequireDeviceManagerRole()
    {
        Assert.Equal(RoleNames.DeviceManagers, RolesOn(typeof(DeviceApiController), "DeviceUpdate"));
        Assert.Equal(RoleNames.DeviceManagers, RolesOn(typeof(DeviceApiController), "DeviceDelete"));
        Assert.Equal(RoleNames.DeviceManagers, RolesOn(typeof(DeviceApiController), "DeviceConfigSensorUpdate"));
        Assert.Equal(RoleNames.DeviceManagers, RolesOn(typeof(DeviceApiController), "DeviceConfigControllerUpdate"));
    }

    [Fact]
    public void UserWrites_RequireUserManagerRole()
    {
        Assert.Equal(RoleNames.UserManagers, RolesOn(typeof(UserApiController), "UserAdd"));
        Assert.Equal(RoleNames.UserManagers, RolesOn(typeof(UserApiController), "UserUpdate"));
        Assert.Equal(RoleNames.UserManagers, RolesOn(typeof(UserApiController), "Delete"));
    }

    [Fact]
    public void RoleGranting_StaysAdminOnly_NeverJustUserManager()
    {
        // A Tenant User must not be able to hand themselves Tenant admin - see UserRolesSet.
        Assert.Equal(RoleNames.Admins, RolesOn(typeof(UserApiController), "UserRolesSet"));
        Assert.DoesNotContain(RoleNames.TenantUser, RoleNames.Admins);
        Assert.DoesNotContain(RoleNames.GlobalUser, RoleNames.Admins);
    }

    [Fact]
    public void GatewayController_MappingWrites_RequireAntiForgeryToken()
    {
        var controller = typeof(api.Controllers.View.GatewayController);
        Assert.Contains(controller.GetMethod("MappingAdd")!.GetCustomAttributes(inherit: true),
            a => a is Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute);
        Assert.Contains(controller.GetMethod("MappingDelete")!.GetCustomAttributes(inherit: true),
            a => a is Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute);
    }

}
