using System.Security.Claims;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
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

    private DeviceApiController NewDeviceController() => new(_repo.Object, _cache.Object);
    private UserApiController NewUserController() => new(_repo.Object, _cache.Object);

    /// <summary>Gives a bare (non-DI-constructed) controller the JWT claims an [Authorize] action reads via HttpContext.User.</summary>
    private static void SetCaller(ControllerBase controller, string role, int? tenantId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim("TenantID", tenantId.ToString() ?? "")
        });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
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
            DevicePin = 1234,
            MacAddress = "AABBCCDDEEFF",
        });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        Assert.Equal("Wrong user or pin", obj.Value);
        _repo.Verify(r => r.DeviceAddAsync(It.IsAny<Device>()), Times.Never);
    }

    // ---- UserApiController.UserRegistration ------------------------------------------------

    [Fact]
    public async Task UserRegistration_NewTenantName_CreatesTenantAndBecomesAdmin()
    {
        _repo.Setup(r => r.TenantGetAsync("Acme")).ReturnsAsync(false);
        _repo.Setup(r => r.TenantAddAsync("Acme")).ReturnsAsync(42);

        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = NewUserController();
        var value = new UserRegistration { Email = "owner@acme.local", Username = "owner", Password = "pw", TenantName = "Acme" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(42, capturedUser!.TenantID);
        Assert.Equal(0, capturedUser.UserGroupID); // admin on a brand new tenant
        Assert.True(capturedUser.Enabled);
    }

    [Fact]
    public async Task UserRegistration_ExistingTenantName_JoinsAsDisabledRegularUser()
    {
        _repo.Setup(r => r.TenantGetAsync("Acme")).ReturnsAsync(true);
        _repo.Setup(r => r.TenantGetIdAsync("Acme")).ReturnsAsync(42);

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
        Assert.False(capturedUser.Enabled);        // waits for that tenant's admin to enable them
    }

    // ---- UserApiController.UserLogin -----------------------------------------------------

    [Fact]
    public async Task UserLogin_CorrectCredentials_ReturnsOkWithToken()
    {
        const string password = "hunter2!";
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash(password, salt);

        _repo.Setup(r => r.UserGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", UserRoleID = 1, TenantID = 0 });
        _repo.Setup(r => r.UserSecretGetAsync(null, "alice@example.com", null))
             .ReturnsAsync(new UserSecret { PwdHash = hash, PwdSalt = salt });
        _repo.Setup(r => r.UserRoleGetAsync())
             .ReturnsAsync(new List<UserRole> { new() { IDUserRole = 1, RoleName = "user" } });
        _repo.Setup(r => r.RefreshTokenAddAsync(5, It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(1);

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "alice@example.com", Password = password });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.Equal("alice@example.com", login.Email);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.Equal("user", JwtTokenProvider.ValidateToken(login.Token!)); // token valid, role claim round-trips
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
             .ReturnsAsync(new User { IDUser = 5, Email = "alice@example.com", UserRoleID = 1, TenantID = 0 });
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
        Assert.Equal("user", JwtTokenProvider.ValidateToken(login.Token!));
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

        var controller = NewUserController();
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { TenantID = 999, Email = "x@test.local", Username = "x", Password = "pw", UserGroupID = 1, Enabled = true };
        var result = await controller.UserAdd(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(24, capturedUser!.TenantID); // not 999 from the payload
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
}

/// <summary>
/// Regression guard: DELETE /api/SensorData must stay admin-only. [Authorize] is middleware, so
/// this asserts the attribute is present rather than driving a request.
/// </summary>
public class SensorDataAuthorizationTests
{
    [Fact]
    public void Delete_RequiresAdminAuthorization()
    {
        var del = typeof(SensorDataController).GetMethod("Delete");
        Assert.NotNull(del);

        var authorize = del!.GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal("admin", authorize!.Roles);
    }

    [Fact]
    public void Delete_IsAnHttpDeleteEndpoint()
    {
        var del = typeof(SensorDataController).GetMethod("Delete")!;
        Assert.Contains(del.GetCustomAttributes(inherit: true), a => a is HttpDeleteAttribute);
    }
}
