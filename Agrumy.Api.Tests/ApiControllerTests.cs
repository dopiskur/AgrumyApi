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

        var controller = NewUserController();
        var result = await controller.UserLogin(new UserLogin { Login = "alice@example.com", Password = password });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.Equal("alice@example.com", login.Email);
        Assert.False(string.IsNullOrEmpty(login.Token));
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
