using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// <summary>Server-wide settings (roadmap #10) - currently just the hysteresis defaults new
    /// devices are seeded with.</summary>
    [Authorize(Roles = "admin")]
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
