using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Server-wide settings (currently just the hysteresis defaults new devices are
    /// seeded with - roadmap #10). Admin-only; there is exactly one row (id 1), auto-created on
    /// first read.</summary>
    [Route("api/ServerConfig")]
    public class ServerConfigApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        // #66 Phase 2: these are SERVER-WIDE settings, so the old "any tenant's admin can edit
        // them" behaviour was a hole - now Global admin only. The attribute stays at the wider
        // "admin" alias so an account the #66 migration missed reaches the inline check, where
        // CallerIsGlobalAdmin's legacy fallback (tenant-0 admin) still lets it through.

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ServerConfig>> Get()
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide settings require the Global admin role");
            }
            return Ok(await Repo.ServerConfigGetAsync(1));
        }

        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Update([FromBody] ServerConfig config)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide settings require the Global admin role");
            }
            config.IDServerConfig = 1; // single global row - the form never chooses this
            await Repo.ServerConfigUpdateAsync(config);
            return Ok();
        }

        /// <summary>Roadmap #64: the Register page is anonymous and must not call the admin-only
        /// Get() above just to know whether to show a "create a new tenant" field - this exposes
        /// only that one flag.</summary>
        [HttpGet("Public")]
        [AllowAnonymous]
        public async Task<ActionResult<PublicServerConfig>> GetPublic()
        {
            ServerConfig config = await Repo.ServerConfigGetAsync(1);
            return Ok(new PublicServerConfig { AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation });
        }
    }
}
