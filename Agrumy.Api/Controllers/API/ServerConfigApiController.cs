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
        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ServerConfig>> Get() =>
            Ok(await Repo.ServerConfigGetAsync(1));

        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Update([FromBody] ServerConfig config)
        {
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
            return Ok(new PublicServerConfig { AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation ?? Config.allowSelfServiceTenantCreation });
        }
    }
}
