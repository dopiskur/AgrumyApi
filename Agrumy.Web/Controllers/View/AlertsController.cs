using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Alerts settings, split out of Server Settings into their own sidebar page (roadmap #338) -
    /// still the same ServerConfig model/endpoint, just a different View surfacing a different
    /// subset of its fields (same pattern as the Firmware page splitting off FirmwareSource et al.).
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class AlertsController(IApi api) : Controller
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

            try
            {
                await api.ServerConfigUpdate(serverConfig);
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(nameof(ServerConfig.ProblemEventExpiryHours), ex.Body);
                return View(serverConfig);
            }

            TempData["Message"] = "Alert settings saved.";
            return RedirectToAction(nameof(Index));
        }
    }
}
