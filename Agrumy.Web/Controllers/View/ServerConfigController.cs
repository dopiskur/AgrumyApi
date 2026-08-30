using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// <summary>Server-wide settings (roadmap #10). #66 Phase 2: Global admin only - these apply to
    /// every tenant, so a single tenant's admin editing them was a hole the binary "admin" role
    /// couldn't express. Matches the API-side check in ServerConfigApiController.</summary>
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class ServerConfigController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.ServerConfigGet());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(ServerConfig serverConfig)
        {
            if (!ModelState.IsValid)
            {
                return View(serverConfig);
            }

            await api.ServerConfigUpdate(serverConfig);
            TempData["Message"] = "Server settings saved.";
            return RedirectToAction(nameof(Index));
        }
    }
}
