using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class ServerConfigController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            ServerConfig config = await api.ServerConfigGet();
            ViewBag.TimeZones = TimeZoneOptions(config.ScheduleTimeZone);
            return View(config);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(ServerConfig serverConfig)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.TimeZones = TimeZoneOptions(serverConfig.ScheduleTimeZone);
                return View(serverConfig);
            }

            try
            {
                await api.ServerConfigUpdate(serverConfig);
            }
            catch (ApiException ex)
            {
                // Route the API's error text to the field it's actually about, else it lands under the time zone by default.
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
                                                : nameof(ServerConfig.ScheduleTimeZone);
                ModelState.AddModelError(field, ex.Body);
                ViewBag.TimeZones = TimeZoneOptions(serverConfig.ScheduleTimeZone);
                return View(serverConfig);
            }

            TempData["Message"] = "Server settings saved.";
            return RedirectToAction(nameof(Index));
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

        // A blank ScheduleTimeZone is a real, intentional state (schedules evaluate as UTC), unlike a user's display TimeZone.
        private static List<SelectListItem> TimeZoneOptions(string? selected)
        {
            var options = new List<SelectListItem> { new("(not set - schedules evaluate as UTC)", "") };
            options.AddRange(TimeZoneHelper.GetTimeZoneOptions()
                .Select(o => new SelectListItem(o.DisplayName, o.Id, string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase))));
            return options;
        }
    }
}
