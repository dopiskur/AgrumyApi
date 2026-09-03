using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    /// <summary>Server-wide settings (roadmap #10). #66 Phase 2: Global admin only - these apply to
    /// every tenant, so a single tenant's admin editing them was a hole the binary "admin" role
    /// couldn't express. Matches the API-side check in ServerConfigApiController.</summary>
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
                // Roadmap #94/#36: the API also rejects a bad firmware source/repository/URL or an
                // out-of-range WaterPump safety limit - route each to the field it is actually
                // about, else it lands under the time zone by default.
                string field = ex.Body.Contains("firmware", StringComparison.OrdinalIgnoreCase) || ex.Body.Contains("GitHub", StringComparison.OrdinalIgnoreCase)
                    ? nameof(ServerConfig.FirmwareSource)
                    : ex.Body.Contains("cooldown", StringComparison.OrdinalIgnoreCase)
                        ? nameof(ServerConfig.WaterPumpCooldownSeconds)
                        : ex.Body.Contains("WaterPump", StringComparison.OrdinalIgnoreCase)
                            ? nameof(ServerConfig.WaterPumpMaxRunSeconds)
                            : ex.Body.Contains("retention", StringComparison.OrdinalIgnoreCase)
                                ? nameof(ServerConfig.SensorDataRetentionDays)
                                // Roadmap #11.
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

        /// <summary>Roadmap #126: whether to show the MariaDB-only "shrink files on disk?" dialog
        /// before a Purge confirmation - fetched once by the page's own JS, same AJAX-target
        /// convention as DeviceController.IssueCommand (device-commands.js).</summary>
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

        /// <summary>Roadmap #39: same source/shape as ProfileController's per-user dropdown - one
        /// extra "not set" option up top, since a blank ScheduleTimeZone (unlike a user's display
        /// TimeZone) is a real, intentional state (see api.Models.ServerConfig's comment).</summary>
        private static List<SelectListItem> TimeZoneOptions(string? selected)
        {
            var options = new List<SelectListItem> { new("(not set - schedules evaluate as UTC)", "") };
            options.AddRange(TimeZoneHelper.GetTimeZoneOptions()
                .Select(o => new SelectListItem(o.DisplayName, o.Id, string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase))));
            return options;
        }
    }
}
