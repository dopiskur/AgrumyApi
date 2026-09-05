using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize(Roles = RoleNames.DeviceManagers)]
    public class WifiConfigController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.DiscoveryWifiConfigsGet());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Add(TenantWifiConfig config)
        {
            try
            {
                await api.DiscoveryWifiConfigAdd(config);
                TempData["Message"] = "WiFi network saved.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Update(int idTenantWifiConfig, TenantWifiConfig config)
        {
            try
            {
                await api.DiscoveryWifiConfigUpdate(idTenantWifiConfig, config);
                TempData["Message"] = "WiFi network updated.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(int idTenantWifiConfig)
        {
            try
            {
                await api.DiscoveryWifiConfigDelete(idTenantWifiConfig);
                TempData["Message"] = "WiFi network removed.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
