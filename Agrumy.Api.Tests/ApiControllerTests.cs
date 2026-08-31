using System.Security.Claims;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Controller tests with a mocked <see cref="IRepository"/> / <see cref="ICache"/> passed straight
/// to the controller constructor. No database, no MVC pipeline (so the global DbExceptionFilter is
/// not in play - its behaviour is covered by <see cref="DbExceptionFilterTests"/>).
/// </summary>
public class ApiControllerTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    // Loose: most tests here don't care whether/how an activation or approval email was
    // dispatched - only the handful that assert on it (see the roadmap #24/#63 section) add setups.
    private readonly Mock<INotificationDispatcher> _notifications = new();

    private DeviceApiController NewDeviceController() => new(_repo.Object, _cache.Object);
    private UserApiController NewUserController() => new(_repo.Object, _cache.Object, _notifications.Object);

    /// <summary>Gives a bare (non-DI-constructed) controller the JWT claims an [Authorize] action reads via HttpContext.User.</summary>
    private static void SetCaller(ControllerBase controller, string role, int? tenantId) =>
        SetCallerRoles(controller, tenantId, role);

    /// <summary>#66: same, but with the full multi-role claim set a real post-#66 token carries
    /// (legacy alias first, then the granular roles - order matters only for CallerRole).</summary>
    private static void SetCallerRoles(ControllerBase controller, int? tenantId, params string[] roles)
    {
        var claims = new List<Claim> { new("TenantID", tenantId.ToString() ?? "") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
    }

    // ---- DeviceApiController.DeviceGet ----------------------------------------------------

    [Fact]
    public async Task DeviceGet_HappyPath_ReturnsOkWithDevice()
    {
        var device = new Device { IDDevice = 42, DeviceName = "greenhouse-1" };
        _repo.Setup(r => r.DeviceGetAsync(7, 42, null, null)).ReturnsAsync(device);

        var controller = NewDeviceController();
        SetCaller(controller, "user", 7); // DeviceGet scopes to the caller's tenant
        var result = await controller.DeviceGet(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(device, ok.Value);
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
        var result = await controller.DeviceUpdate(new Device { IDDevice = 99 });

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

    // ---- roadmap #7/#8: fleet online threshold ---------------------------------------------

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
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 3, ConfigVersion = 66 });
        _repo.Setup(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.IsAny<DeviceConfigPoll>()))
             .Returns(Task.CompletedTask);
        _cache.Setup(c => c.GetDeviceCacheAsync("api-guid")).ReturnsAsync(new DeviceCache { ConfigVersion = 66 });

        var result = await controller.GetConfig(new DeviceConfigPoll { ConfigVersion = 66, Rssi = -60 });

        // The matching version path must still land the heartbeat - that's what LastSeenAt is for.
        _repo.Verify(r => r.DeviceDiagnosticUpsertAsync(500, 3, It.Is<DeviceConfigPoll>(p => p.Rssi == -60)), Times.Once);
        Assert.IsType<OkResult>(result.Result); // empty body: device is up to date
    }

    // ---- roadmap #70: PIN expiry + multi-use (follow-up: no longer consumed on first use) ----

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

        var result = await NewDeviceController().DeviceRegistration(PinRegistration("abc234"));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeviceRegistration_ValidPin_NotConsumed_ReusableForASecondDevice()
    {
        // Roadmap #70 follow-up: single-use made bulk sensor registration a chore of regenerating
        // the PIN between every device - it must now survive repeated use until its own expiry.
        StubOwner("ABC234", DateTime.UtcNow.AddHours(1));
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "AABBCCDDEEFF"))
             .ReturnsAsync(new Device { IDDevice = 500, TenantID = 1, DeviceSensorEnabled = false, DeviceControllerEnabled = false });
        _repo.Setup(r => r.DeviceGetAsync(1, null, null, "112233445566"))
             .ReturnsAsync(new Device { IDDevice = 501, TenantID = 1, DeviceSensorEnabled = false, DeviceControllerEnabled = false });

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

    // ---- UserApiController.UserRegistration ------------------------------------------------

    /// <summary>Common post-UserAddAsync plumbing every UserRegistration call now goes through
    /// (roadmap #24/#66): a lookup to recover the freshly-inserted IDUser, an activation token
    /// write, and a starting role assignment. None of these are the point of most of these tests,
    /// so they're stubbed permissively here.</summary>
    private void StubActivationPlumbing(string email, int idUser)
    {
        _repo.Setup(r => r.UserGetAsync(null, email, null)).ReturnsAsync(new User { IDUser = idUser, Email = email });
        _repo.Setup(r => r.UserSetActivationTokenAsync(idUser, It.IsAny<string>(), It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserRolesSetAsync(idUser, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task UserRegistration_NewTenantName_CreatesTenantAndBecomesAdmin()
    {
        _repo.Setup(r => r.TenantGetAsync("AcmeCorp")).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { AllowSelfServiceTenantCreation = true });
        _repo.Setup(r => r.TenantAddAsync("AcmeCorp")).ReturnsAsync(42);
        StubActivationPlumbing("owner@acme.local", 1);

        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "pw", TenantName = "AcmeCorp" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(42, capturedUser!.TenantID);
        Assert.Equal(0, capturedUser.UserGroupID); // admin on a brand new tenant
        Assert.True(capturedUser.Enabled);          // nobody else exists yet to approve them (roadmap #63)
        Assert.False(capturedUser.EmailVerified);   // still needs to click the activation link (roadmap #24)
    }

    [Fact]
    public async Task UserRegistration_UnknownTenantName_SelfServiceDisabled_Returns403AndDoesNotCreateTenant()
    {
        _repo.Setup(r => r.TenantGetAsync("AcmeCorp")).ReturnsAsync(false);
        _repo.Setup(r => r.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { AllowSelfServiceTenantCreation = false });

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "pw", TenantName = "AcmeCorp" };
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
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "pw", TenantName = "abc" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _repo.Verify(r => r.TenantAddAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UserRegistration_ExistingTenantName_JoinsAsDisabledRegularUser()
    {
        _repo.Setup(r => r.TenantGetAsync("Acme")).ReturnsAsync(true);
        _repo.Setup(r => r.TenantGetIdAsync("Acme")).ReturnsAsync(42);
        StubActivationPlumbing("member@acme.local", 2);

        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var value = new UserRegistration { Email = "member@acme.local", Username = "member", Password = "pw", TenantName = "Acme" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(42, capturedUser!.TenantID); // joins the existing tenant, no new one created
        Assert.Equal(1, capturedUser.UserGroupID); // regular user, not admin
        Assert.False(capturedUser.Enabled);        // waits for that tenant's admin to enable them (roadmap #63)
    }

    [Fact]
    public async Task UserRegistration_ExistingTenantZero_AutoEnabled_NoOneToApprove()
    {
        // TenantID 0 has no owning admin to ask, same "nobody to approve" reasoning as a brand-new
        // tenant's own creator above - roadmap #63.
        _repo.Setup(r => r.TenantGetAsync("default")).ReturnsAsync(true);
        _repo.Setup(r => r.TenantGetIdAsync("default")).ReturnsAsync(0);
        StubActivationPlumbing("newbie@example.com", 3);

        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var value = new UserRegistration { Email = "newbie@example.com", Username = "newbie", Password = "pw", TenantName = "default" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(capturedUser!.Enabled);
    }

    // ---- UserApiController.UserLogin -----------------------------------------------------

    [Fact]
    public async Task UserLogin_CorrectCredentials_ReturnsOkWithToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", UserRoleID = 1, TenantID = 0, EmailVerified = true, Enabled = true });
        _repo.Setup(r => r.UserSecretGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });
        // Roadmap #66: the real source of truth going forward - a non-empty set here means
        // UserRoleGetAsync()'s legacy-fallback path is never touched.
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
        // Legacy "user" alias first (pre-#66 checks read the first role claim), then the real role.
        Assert.Equal(new[] { "user", RoleNames.TenantReader }, JwtTokenProvider.ValidateToken(login.Token!));
    }

    [Fact]
    public async Task UserLogin_WrongPassword_Returns401()
    {
        string salt = AuthenticationProvider.GetSalt();
        string hashForRealPassword = AuthenticationProvider.GetHash("the-real-password", salt);

        _repo.Setup(r => r.UserGetAsync(null, "bob@example.com", null))
             .ReturnsAsync(new User { IDUser = 6, Email = "bob@example.com", UserRoleID = 1, TenantID = 0 });
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

    // ---- UserApiController.UserLogin - roadmap #68 (Enabled/EmailVerified were never checked) ----

    [Fact]
    public async Task UserLogin_CorrectPassword_EmailNotVerified_Returns403_NotAToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new User { IDUser = 7, Email = "pending@example.com", UserRoleID = 1, TenantID = 0, EmailVerified = false, Enabled = true });
        _repo.Setup(r => r.UserSecretGetAsync(null, "pending@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "pending@example.com", Password = password });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        // MockBehavior.Strict: an un-set-up RefreshTokenAddAsync call would throw, so reaching this
        // point already proves no token was ever issued.
    }

    [Fact]
    public async Task UserLogin_EmailVerified_ButNotEnabled_Returns403_NotAToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "waiting@example.com", null))
             .ReturnsAsync(new User { IDUser = 8, Email = "waiting@example.com", UserRoleID = 1, TenantID = 1, EmailVerified = true, Enabled = false });
        _repo.Setup(r => r.UserSecretGetAsync(null, "waiting@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "waiting@example.com", Password = password });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    // ---- UserApiController.RefreshToken / RevokeRefreshToken --------------------------

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
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", UserRoleID = 1, TenantID = 0, EmailVerified = true, Enabled = true });
        // Roadmap #66: empty userUserRole set exercises the legacy UserGroupID-derived fallback in
        // ResolveCallerTokenRolesAsync - covers an account the migration somehow missed.
        _repo.Setup(r => r.UserRoleNamesGetAsync(5)).ReturnsAsync(new List<string>());
        _repo.Setup(r => r.UserRoleGetAsync())
             .ReturnsAsync(new List<UserRole> { new() { IDUserRole = 1, RoleName = "user" } });
        _repo.Setup(r => r.RefreshTokenRotateAsync(hash, It.IsAny<string>(), It.IsAny<DateTime>()))
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var result = await controller.RefreshToken(new RefreshTokenRequest { RefreshToken = presented });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.NotEqual(presented, login.RefreshToken); // rotated, not reissued
        Assert.Equal(new[] { "user" }, JwtTokenProvider.ValidateToken(login.Token!));
        _repo.Verify(r => r.RefreshTokenRotateAsync(hash, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
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
        // MockBehavior.Strict: an un-set-up RefreshTokenRotateAsync/RevokeAllForUserAsync call would
        // throw, so reaching this point already proves neither was called.
    }

    [Fact]
    public async Task RefreshToken_ValidToken_ButUserDisabledSinceIssue_Returns403_DoesNotRotate()
    {
        // Roadmap #68: a refresh token issued before an admin disabled the account (or before email
        // verification, hypothetically) must not keep minting fresh access tokens forever.
        string hash = HashRefreshToken("still-technically-valid");
        _repo.Setup(r => r.RefreshTokenGetAsync(hash)).ReturnsAsync(
            new RefreshTokenInfo { UserID = 11, ExpiresAt = DateTime.UtcNow.AddDays(10), RevokedAt = null });
        _repo.Setup(r => r.UserGetAsync(11, null, null))
             .ReturnsAsync(new User { IDUser = 11, Email = "disabled@example.com", UserRoleID = 1, TenantID = 1, EmailVerified = true, Enabled = false });

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

    // ---- tenant scoping ---------------------------------------------------------------

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
    public async Task UserGroupGet_UnknownId_Returns404()
    {
        _repo.Setup(r => r.UserGroupGetAsync(999)).ReturnsAsync((UserGroup?)null);

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserGroupGet(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UsersGet_UsesCallerTenant_NotHardcodedDefault()
    {
        // Strict mock: if the controller passed a hard-coded 0 instead of the caller's claim,
        // this setup wouldn't match and the call would throw.
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

        var controller = NewUserController();
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { TenantID = 999, Email = "x@test.local", Username = "x", Password = "pw", UserGroupID = 1, Enabled = true };
        var result = await controller.UserAdd(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(24, capturedUser!.TenantID); // not 999 from the payload
    }

    [Fact]
    public async Task UserAdd_AdminGroup_TenantAdmin_SeedsTenantAdminRole()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "boss@test.local", null)).ReturnsAsync(new User { IDUser = 100, Email = "boss@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(100, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 24); // NOT tenant 0 - a regular Tenant admin, not Global admin
        var value = new UserAdd { Email = "boss@test.local", Username = "boss", Password = "pw", UserGroupID = 0, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.TenantAdmin }, seededRoles);
    }

    [Fact]
    public async Task UserAdd_AdminGroup_CallerIsGlobalAdmin_SeedsGlobalAdminRole()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "boss@test.local", null)).ReturnsAsync(new User { IDUser = 101, Email = "boss@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(101, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 0); // Global admin
        var value = new UserAdd { Email = "boss@test.local", Username = "boss", Password = "pw", UserGroupID = 0, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.GlobalAdmin }, seededRoles);
    }

    [Fact]
    public async Task UserAdd_RegularGroup_SeedsTenantReaderRole()
    {
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.UserGetAsync(null, "newbie@test.local", null)).ReturnsAsync(new User { IDUser = 102, Email = "newbie@test.local" });
        List<string>? seededRoles = null;
        _repo.Setup(r => r.UserRolesSetAsync(102, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => seededRoles = roles.ToList())
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { Email = "newbie@test.local", Username = "newbie", Password = "pw", UserGroupID = 1, Enabled = true };
        await controller.UserAdd(value);

        Assert.Equal(new[] { RoleNames.TenantReader }, seededRoles);
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
        // Regression guard: the tenant check must fire for any change, not only Enabled/UserGroupID.
        _repo.Setup(r => r.UserGetAsync(50, null, null))
             .ReturnsAsync(new User { IDUser = 50, TenantID = 99, Email = "target@test.local" });

        var controller = NewUserController();
        SetCaller(controller, "admin", 1);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, Email = "hijacked@evil.local" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }

    // ---- UserApiController.Activate / ResendActivation - roadmap #24/#63 -------------------

    [Fact]
    public async Task Activate_ValidToken_TenantZero_VerifiesAndReportsCanSignIn()
    {
        const string plaintext = "the-activation-token";
        string hash = HashRefreshToken(plaintext);
        _repo.Setup(r => r.UserActivateAsync(hash))
             .ReturnsAsync(new User { IDUser = 1, Email = "a@example.com", TenantID = 0, Enabled = true });

        var controller = NewUserController();
        var result = await controller.Activate(plaintext);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("now sign in", (string)ok.Value!);
        // Loose mock: no DispatchAsync setup needed/expected - tenant 0 has nobody to notify.
    }

    [Fact]
    public async Task Activate_ValidToken_ExistingNonZeroTenant_StillDisabled_NotifiesTenantAdmins()
    {
        const string plaintext = "the-activation-token";
        string hash = HashRefreshToken(plaintext);
        var activatedUser = new User { IDUser = 2, Email = "member@acme.local", Username = "member", TenantID = 42, Enabled = false };
        _repo.Setup(r => r.UserActivateAsync(hash)).ReturnsAsync(activatedUser);
        _repo.Setup(r => r.TenantAdminsGetAsync(42))
             .ReturnsAsync(new List<User> { new() { Email = "admin@acme.local" } });

        var controller = NewUserController();
        var result = await controller.Activate(plaintext);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("administrator has been notified", (string)ok.Value!);
        _notifications.Verify(n => n.DispatchAsync(
            It.Is<Notification>(msg => msg.Recipient.Email == "admin@acme.local"),
            It.IsAny<CancellationToken>()), Times.Once);
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
        // Strict mock: an un-set-up UserActivateAsync call would throw, so reaching this point
        // already proves the controller short-circuited before touching the repo.
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
        // Strict mock: an un-set-up ServerConfigGetAsync/UserIssueActivationTokenAsync call would
        // throw - reaching this point proves EmailVerified==true short-circuited before either ran.
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
        _notifications.Verify(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- UserApiController - roadmap #65 (minimal Global admin) ---------------------------

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
        // Strict mock: an un-set-up UsersGetAsync(0) call would throw, proving the "all tenants"
        // path was taken instead of the normal tenant-scoped one.
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
        _repo.Setup(r => r.UserDeleteAsync(50)).ReturnsAsync(true);

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.Delete(50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("User deleted", ok.Value);
    }

    // ---- UserApiController.UserRolesGet / UserRolesSet - roadmap #66 (composable roles) ----

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
        List<string>? written = null;
        _repo.Setup(r => r.UserRolesSetAsync(50, It.IsAny<IEnumerable<string>>()))
             .Callback<int, IEnumerable<string>>((_, roles) => written = roles.ToList())
             .Returns(Task.CompletedTask);

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
        _repo.Setup(r => r.UserRolesSetAsync(50, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCaller(controller, "admin", 0);
        var result = await controller.UserRolesSet(new UserRolesUpdate { IDUser = 50, RoleNames = new List<string> { RoleNames.GlobalReader } });

        Assert.IsType<OkResult>(result);
    }

    // ---- #66 Phase 2: granular capability scoping (inline logic, attributes covered below) ----

    private ServerConfigApiController NewServerConfigController() => new(_repo.Object, _cache.Object);
    private SensorDataController NewSensorDataController() => new(_repo.Object, _cache.Object);

    [Fact]
    public async Task DeviceUpdate_TenantDevice_OwnTenant_Succeeds()
    {
        // ApiId left null so RefreshConfigVersionCacheAsync's early-return path fires - the cache
        // side of an update is not what this test is about.
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 1 });
        _repo.Setup(r => r.DeviceUpdateAsync(It.IsAny<Device>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.DeviceUpdate(new Device { IDDevice = 8 });

        Assert.True(result.Value);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Once);
    }

    [Fact]
    public async Task DeviceUpdate_TenantDevice_ForeignTenant_Returns403()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.DeviceUpdate(new Device { IDDevice = 8 });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.DeviceUpdateAsync(It.IsAny<Device>()), Times.Never);
    }

    [Fact]
    public async Task DeviceUpdate_GlobalDevice_CrossesTenants()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });
        _repo.Setup(r => r.DeviceUpdateAsync(It.IsAny<Device>())).Returns(Task.CompletedTask);

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.GlobalDevice);
        var result = await controller.DeviceUpdate(new Device { IDDevice = 8 });

        Assert.True(result.Value);
    }

    [Fact]
    public async Task DeviceUpdate_GlobalReader_ForeignTenant_Returns403_ReadNeverImpliesWrite()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(8)).ReturnsAsync(new Device { IDDevice = 8, TenantID = 99 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.GlobalReader);
        var result = await controller.DeviceUpdate(new Device { IDDevice = 8 });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task DevicesGet_GlobalReader_SeesEveryTenant()
    {
        // Strict mock: an un-set-up DevicesGetAsync(3) call would throw, proving the all-tenants
        // path was taken.
        _repo.Setup(r => r.DevicesGetAllAsync()).ReturnsAsync(new List<Device>
        {
            new() { IDDevice = 1, TenantID = 0 }, new() { IDDevice = 2, TenantID = 7 },
        });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 3, "user", RoleNames.GlobalReader);
        var result = await controller.DevicesGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Device>>(ok.Value).Count());
    }

    [Fact]
    public async Task DevicesGet_TenantReader_ScopedToOwnTenant()
    {
        _repo.Setup(r => r.DevicesGetAsync(3)).ReturnsAsync(new List<Device> { new() { IDDevice = 1, TenantID = 3 } });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 3, "user", RoleNames.TenantReader);
        var result = await controller.DevicesGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Device>>(ok.Value));
    }

    [Fact]
    public async Task DeviceGet_GlobalReader_UsesUnfilteredLookup()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(42)).ReturnsAsync(new Device { IDDevice = 42, TenantID = 9 });

        var controller = NewDeviceController();
        SetCallerRoles(controller, 1, "user", RoleNames.GlobalReader);
        var result = await controller.DeviceGet(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(42, ((Device)ok.Value!).IDDevice);
    }

    [Fact]
    public async Task DeviceDelete_GlobalAdmin_ForeignTenant_DeletesWithTheDevicesOwnTenant()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 99 });
        _repo.Setup(r => r.DeviceDeleteAsync(7, 99)).Returns(Task.CompletedTask);

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
        _repo.Setup(r => r.UserUpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantUser);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task UserUpdate_TenantDevice_Returns403_DeviceGrantNeverImpliesUserManagement()
    {
        // Defence in depth: even if the attribute somehow let a device-only grant through, the
        // inline CallerManagesUsers check must still refuse.
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 1, Email = "x@test.local" });

        var controller = NewUserController();
        SetCallerRoles(controller, 1, "user", RoleNames.TenantReader, RoleNames.TenantDevice);
        var result = await controller.UserUpdate(new UserUpdate { IDUser = 50, FirstName = "New" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ServerConfig_TenantAdmin_Returns403_ServerWideSettingsAreGlobalOnly()
    {
        // Strict mock: an un-set-up ServerConfigGetAsync call would throw, so reaching the asserts
        // proves the request was refused before touching the repo.
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
}

/// <summary>
/// Regression guards for the #66 Phase 2 role gates. [Authorize] is middleware, so these assert
/// the attribute's role list rather than driving a request - the inline tenant-scoping logic is
/// covered by the direct-call tests above.
/// </summary>
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
        Assert.Equal(RoleNames.Admins, RolesOn(typeof(UserApiController), "UserGroupAdd"));
        Assert.Equal(RoleNames.Admins, RolesOn(typeof(UserApiController), "UserGroupDelete"));
        Assert.DoesNotContain(RoleNames.TenantUser, RoleNames.Admins);
        Assert.DoesNotContain(RoleNames.GlobalUser, RoleNames.Admins);
    }
}
