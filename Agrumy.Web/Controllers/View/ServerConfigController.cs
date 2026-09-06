using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
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

            try
            {
                await api.ServerConfigUpdate(serverConfig);
            }
            catch (ApiException ex)
            {
                // Route the API's error text to the field it's actually about, else it lands under firmware source by default.
                string field = ex.Body.Contains("firmware", StringComparison.OrdinalIgnoreCase) || ex.Body.Contains("GitHub", StringComparison.OrdinalIgnoreCase)
                    ? nameof(ServerConfig.FirmwareSource)
                    : ex.Body.Contains("cooldown", StringComparison.OrdinalIgnoreCase)
                        ? nameof(ServerConfig.WaterPumpCooldownSeconds)
                        : ex.Body.Contains("WaterPump", StringComparison.OrdinalIgnoreCase)
                            ? nameof(ServerConfig.WaterPumpMaxRunSeconds)
                            : ex.Body.Contains("retention", StringComparison.OrdinalIgnoreCase)
                                ? nameof(ServerConfig.SensorDataRetentionDays)
                                : ex.Body.Contains("latitude", StringComparison.OrdinalIgnoreCase)
                                    ? nameof(ServerConfig.WeatherLocationLat)
                                    : ex.Body.Contains("longitude", StringComparison.OrdinalIgnoreCase)
                                        ? nameof(ServerConfig.WeatherLocationLon)
                                        : ex.Body.Contains("poll interval", StringComparison.OrdinalIgnoreCase)
                                            ? nameof(ServerConfig.WeatherPollIntervalMinutes)
                                            : ex.Body.Contains("rain-skip", StringComparison.OrdinalIgnoreCase)
                                                ? nameof(ServerConfig.WeatherRainSkipThreshold)
                                                : ex.Body.Contains("SMTP port", StringComparison.OrdinalIgnoreCase)
                                                    ? nameof(ServerConfig.EmailPort)
                                                    : ex.Body.Contains("email notifications", StringComparison.OrdinalIgnoreCase)
                                                        ? nameof(ServerConfig.EmailHost)
                                                        : ex.Body.Contains("PIN validity", StringComparison.OrdinalIgnoreCase)
                                                            ? nameof(ServerConfig.DevicePinValidMinutes)
                                                            : nameof(ServerConfig.FirmwareSource);
                ModelState.AddModelError(field, ex.Body);
                return View(serverConfig);
            }

            TempData["Message"] = "Server settings saved.";
            return RedirectToAction(nameof(Index));
        }

        /// Sends through the SAVED settings (Save first, then test) - not whatever is currently typed into the unsaved form.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> TestEmail(string toEmail)
        {
            try
            {
                await api.ServerConfigTestEmail(toEmail);
                return Ok();
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, ex.Body);
            }
        }

        [HttpGet]
        public async Task<ActionResult<DataMaintenanceProviderInfo>> DataMaintenanceProvider()
        {
            try
            {
                return Ok(await api.DataMaintenanceProviderGet());
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, ex.Body);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DataMaintenanceOptimize([FromBody] DataMaintenanceRequest request)
        {
            try
            {
                await api.DataMaintenanceOptimize(request);
                return Ok();
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, ex.Body);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DataMaintenancePurge([FromBody] DataPurgeRequest request)
        {
            try
            {
                await api.DataMaintenancePurge(request);
                return Ok();
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, ex.Body);
            }
        }
    }
}
