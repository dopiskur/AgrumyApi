using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using api.Controllers.API;
using api.Dal;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// Controller tests with a mocked IRepository swapped in via RepoFactory's test seam. No database.
/// </summary>
[Collection("RepoFactory")]
public class ApiControllerTests : IDisposable
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    public ApiControllerTests()
    {
        RepoFactory.OverrideForTests(_repo.Object, _cache.Object);
    }

    public void Dispose() => RepoFactory.OverrideForTests(null, null);

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

        var controller = new DeviceApiController(NullLogger<DeviceApiController>.Instance);
        SetCaller(controller, "user", 7); // DeviceGet now scopes to the caller's tenant
        var result = await controller.DeviceGet(42);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(device, ok.Value);
    }

    // ---- DeviceApiController.DeviceDelete (exception -> DbErrorResponse) -----------------

    [Fact]
    public async Task DeviceDelete_WhenRepoThrows_Returns503WithDbErrorResponse()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 0 });
        _repo.Setup(r => r.DeviceDeleteAsync(7, 0)).ThrowsAsync(new InvalidOperationException("db down"));
        _repo.Setup(r => r.ClassifyException(It.IsAny<Exception>())).Returns(DbFailureKind.ConnectionFailure);

        var controller = new DeviceApiController(NullLogger<DeviceApiController>.Instance);
        SetCaller(controller, "admin", 0);
        var result = await controller.DeviceDelete(7);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);

        string json = JsonSerializer.Serialize(obj.Value);
        Assert.Contains("connection_failure", json);
        Assert.Contains("reason", json);
    }

    [Fact]
    public async Task DeviceDelete_WhenRepoThrows_SchemaMissing_ClassifiedAccordingly()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 0 });
        _repo.Setup(r => r.DeviceDeleteAsync(7, 0)).ThrowsAsync(new Exception("Table 'x' doesn't exist"));
        _repo.Setup(r => r.ClassifyException(It.IsAny<Exception>())).Returns(DbFailureKind.SchemaMissing);

        var controller = new DeviceApiController(NullLogger<DeviceApiController>.Instance);
        SetCaller(controller, "admin", 0);
        var result = await controller.DeviceDelete(7);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains("schema_missing", JsonSerializer.Serialize(obj.Value));
    }

    [Fact]
    public async Task DeviceDelete_DifferentTenant_Returns403AndDoesNotCallDelete()
    {
        _repo.Setup(r => r.DeviceGetByIdAsync(7)).ReturnsAsync(new Device { IDDevice = 7, TenantID = 99 });

        var controller = new DeviceApiController(NullLogger<DeviceApiController>.Instance);
        SetCaller(controller, "admin", 1);
        var result = await controller.DeviceDelete(7);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.DeviceDeleteAsync(It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    // ---- UserApiController.UserRegistration (TENANT_ENABLED = true) ---------------------------

    [Fact]
    public async Task UserRegistration_NewTenantName_CreatesTenantAndBecomesAdmin()
    {
        _repo.Setup(r => r.TenantGetAsync("Acme")).ReturnsAsync(false);
        _repo.Setup(r => r.TenantAddAsync("Acme")).ReturnsAsync(42);

        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
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

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        var value = new UserRegistration { Email = "member@acme.local", Username = "member", Password = "pw", TenantName = "Acme" };
        var result = await controller.UserRegistration(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(42, capturedUser!.TenantID); // joins the existing tenant, no new one created
        Assert.Equal(1, capturedUser.UserGroupID); // DEFAULT_ROLEID - regular user, not admin
        Assert.False(capturedUser.Enabled); // waits for that tenant's admin to enable them
    }

    // ---- UserApiController.UserLogin ---------------------------------------------------------

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

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        var result = await controller.UserLogin(new UserLogin { Login = "alice@example.com", Password = password });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var login = Assert.IsType<UserLoginResult>(ok.Value);
        Assert.Equal(5, login.IDUser);
        Assert.Equal("alice@example.com", login.Email);
        Assert.False(string.IsNullOrEmpty(login.Token));
        Assert.Equal("user", JwtTokenProvider.ValidateToken(login.Token!)); // token is valid, role claim round-trips
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

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        var result = await controller.UserLogin(new UserLogin { Login = "bob@example.com", Password = "wrong-password" });

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
    }

    // ---- UserApiController.UserGet ------------------------------------------------------------

    [Fact]
    public async Task UserGet_DifferentTenant_Returns403()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        SetCaller(controller, "admin", 1);
        var result = await controller.UserGet(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
    }

    // ---- UserApiController.UsersGet -----------------------------------------------------------

    [Fact]
    public async Task UsersGet_UsesCallerTenant_NotHardcodedDefault()
    {
        // Strict mock: if the controller still passed the hardcoded DEFAULT_TENANTID (0)
        // instead of the caller's claim, this setup wouldn't match and the call would throw.
        _repo.Setup(r => r.UsersGetAsync(7)).ReturnsAsync(new List<User> { new() { IDUser = 1, TenantID = 7 } });

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        SetCaller(controller, "admin", 7);
        var result = await controller.UsersGet();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var users = Assert.IsAssignableFrom<IList<User>>(ok.Value);
        Assert.Single(users);
    }

    // ---- UserApiController.UserAdd ------------------------------------------------------------

    [Fact]
    public async Task UserAdd_IgnoresPayloadTenantID_UsesCallersTenant()
    {
        User? capturedUser = null;
        _repo.Setup(r => r.UserAddAsync(It.IsAny<User>(), It.IsAny<UserSecret>()))
             .Callback<User, UserSecret>((u, s) => capturedUser = u)
             .Returns(Task.CompletedTask);

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        SetCaller(controller, "admin", 24);
        var value = new UserAdd { TenantID = 999, Email = "x@test.local", Username = "x", Password = "pw", UserGroupID = 1, Enabled = true };
        var result = await controller.UserAdd(value);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(capturedUser);
        Assert.Equal(24, capturedUser!.TenantID); // not 999 from the payload
    }

    // ---- UserApiController.Delete (user) ------------------------------------------------------

    [Fact]
    public async Task UserDelete_DifferentTenant_Returns403AndDoesNotCallDelete()
    {
        _repo.Setup(r => r.UserGetAsync(50, null, null)).ReturnsAsync(new User { IDUser = 50, TenantID = 99 });

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        SetCaller(controller, "admin", 1);
        var result = await controller.Delete(50);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserDeleteAsync(It.IsAny<int?>()), Times.Never);
    }

    // ---- UserApiController.UserUpdate ---------------------------------------------------------

    [Fact]
    public async Task UserUpdate_DifferentTenant_Returns403_EvenForEmailOnlyChange()
    {
        // Regression guard: the tenant check used to only fire for Enabled/UserGroupID changes,
        // letting an admin edit a different tenant's user's Email/Name/Phone unchecked.
        _repo.Setup(r => r.UserGetAsync(50, null, null))
             .ReturnsAsync(new User { IDUser = 50, TenantID = 99, Email = "target@test.local" });

        var controller = new UserApiController(NullLogger<UserApiController>.Instance);
        SetCaller(controller, "admin", 1);
        var value = new UserUpdate { IDUser = 50, Email = "hijacked@evil.local" };
        var result = await controller.UserUpdate(value);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, obj.StatusCode);
        _repo.Verify(r => r.UserUpdateAsync(It.IsAny<User>()), Times.Never);
    }
}

/// <summary>
/// Regression guard for the security fix: DELETE /api/SensorData must stay authorized (admin only).
/// [Authorize] is enforced by middleware, so this asserts the attribute itself is present rather
/// than driving a request.
/// </summary>
public class SensorDataAuthorizationTests
{
    [Fact]
    public void Delete_RequiresAdminAuthorization()
    {
        MethodInfo? del = typeof(SensorDataController).GetMethod("Delete");
        Assert.NotNull(del);

        var authorize = del!.GetCustomAttributes(inherit: true).OfType<AuthorizeAttribute>().SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal("admin", authorize!.Roles);
    }

    [Fact]
    public void Delete_IsAnHttpDeleteEndpoint()
    {
        MethodInfo del = typeof(SensorDataController).GetMethod("Delete")!;
        Assert.Contains(del.GetCustomAttributes(inherit: true), a => a is HttpDeleteAttribute);
    }
}
