using System.Security.Claims;
using api.Controllers.API;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Agrumy.Api.Tests;

/// Covers the tenant-scoping decision AuditLogApiController makes before delegating to IAuditLogRepository - a Global admin sees every tenant, everyone else only their own.
public class AuditLogApiControllerTests
{
    private readonly Mock<IRepository> _repo = new(MockBehavior.Strict);
    private readonly Mock<ICache> _cache = new();

    private AuditLogApiController NewController(int? tenantId, params string[] roles)
    {
        var controller = new AuditLogApiController(_repo.Object, _repo.Object, _cache.Object);
        var claims = new List<Claim> { new("TenantID", tenantId.ToString() ?? "") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
        };
        return controller;
    }

    [Fact]
    public async Task AuditLogGet_GlobalAdmin_PassesNullTenantId_SeesEveryTenant()
    {
        _repo.Setup(r => r.AuditLogGetAsync(null, 200)).ReturnsAsync(new List<AuditLogEntry>());
        var controller = NewController(tenantId: 7, RoleNames.GlobalAdmin);

        var result = await controller.AuditLogGet();

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.AuditLogGetAsync(null, 200), Times.Once);
    }

    [Fact]
    public async Task AuditLogGet_TenantAdmin_PassesOwnTenantId_DoesNotSeeOtherTenants()
    {
        _repo.Setup(r => r.AuditLogGetAsync(7, 200)).ReturnsAsync(new List<AuditLogEntry>());
        var controller = NewController(tenantId: 7, RoleNames.TenantAdmin);

        var result = await controller.AuditLogGet();

        Assert.IsType<OkObjectResult>(result.Result);
        _repo.Verify(r => r.AuditLogGetAsync(7, 200), Times.Once);
        _repo.Verify(r => r.AuditLogGetAsync(null, It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10000)]
    public async Task AuditLogGet_ClampsTakeToValidRange(int requested)
    {
        _repo.Setup(r => r.AuditLogGetAsync(7, It.IsInRange(1, 500, Moq.Range.Inclusive))).ReturnsAsync(new List<AuditLogEntry>());
        var controller = NewController(tenantId: 7, RoleNames.TenantAdmin);

        await controller.AuditLogGet(requested);

        _repo.Verify(r => r.AuditLogGetAsync(7, It.IsInRange(1, 500, Moq.Range.Inclusive)), Times.Once);
    }
}
