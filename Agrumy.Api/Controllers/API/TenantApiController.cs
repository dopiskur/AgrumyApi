using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Tenant Management CRUD (roadmap #196). Read is Global admin or Global reader; write
    /// (create/rename) is Global admin only - a Tenant scope has no meaningful self-management of
    /// its own existence, so this never delegates to a TenantAdmin the way Device/User management does.
    /// [Authorize(Roles=...)] attributes stay at the wide legacy-friendly net (same reasoning as
    /// ServerConfigApiController); the precise decision is the inline CallerIsGlobalAdmin/GlobalReader check.</summary>
    [Route("/api/Tenant")]
    public class TenantApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        [Authorize(Roles = "admin," + RoleNames.GlobalReader)]
        [HttpGet("All")]
        public async Task<ActionResult<IList<Tenant>>> TenantsGet()
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            return Ok(await Repo.TenantsGetAllAsync());
        }

        [Authorize(Roles = "admin," + RoleNames.GlobalReader)]
        [HttpGet]
        public async Task<ActionResult<Tenant>> TenantGet(int idTenant)
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            Tenant? tenant = await Repo.TenantGetByIdAsync(idTenant);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<ActionResult<int>> TenantAdd([FromBody] Tenant tenant)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Creating a tenant requires the Global admin role");
            }
            if (string.IsNullOrWhiteSpace(tenant.TenantName))
            {
                return BadRequest("Tenant name is required.");
            }
            return Ok(await Repo.TenantAddAsync(tenant.TenantName.Trim()));
        }

        [Authorize(Roles = "admin")]
        [HttpPut]
        public async Task<ActionResult> TenantUpdate([FromBody] Tenant tenant)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Renaming a tenant requires the Global admin role");
            }
            if (tenant.IDTenant is null)
            {
                return BadRequest("IDTenant is required.");
            }
            if (string.IsNullOrWhiteSpace(tenant.TenantName))
            {
                return BadRequest("Tenant name is required.");
            }
            tenant.TenantName = tenant.TenantName.Trim();
            await Repo.TenantUpdateAsync(tenant);
            return Ok();
        }
    }
}
