using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize]
    public class DeviceUnitController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.DeviceUnitDashboardGet());

        public async Task<ActionResult> IndexCubes() => PartialView("_UnitCubes", await api.DeviceUnitDashboardGet());

        public async Task<ActionResult> Zones(int idDeviceUnit)
        {
            IList<DeviceUnitZoneDashboard> zones = await api.DeviceUnitZoneDashboardListGet(idDeviceUnit);
            if (zones.Count == 1)
            {
                return RedirectToAction(nameof(Zone), new { idDeviceUnitZone = zones[0].IDDeviceUnitZone });
            }

            string? timeZone = (await api.UserGetSelf()).TimeZone;
            return View(new UnitZonesViewModel
            {
                Unit = await api.DeviceUnitGet(idDeviceUnit),
                Zones = zones,
                DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
                // Last 24h, hourly buckets - same window _ZoneDetails' sparkline trend already uses.
                SensorDataJson = await api.SensorDataUnitAverageGet(idDeviceUnit, 24, 1),
            });
        }

        public async Task<ActionResult> ZonesCubes(int idDeviceUnit) =>
            PartialView("_ZoneCubes", await api.DeviceUnitZoneDashboardListGet(idDeviceUnit));

        public async Task<ActionResult> Zone(int idDeviceUnitZone)
        {
            ZoneViewModel model = await BuildZoneViewAsync(idDeviceUnitZone);
            // Last 24h hourly buckets, only fetched here (not in the 10s-polled ZoneDetails fragment) - the chart lives outside that fragment.
            model.SensorDataJson = await api.SensorDataZoneAverageGet(idDeviceUnitZone, 24, 1);
            return View(model);
        }

        public async Task<ActionResult> ZoneDetails(int idDeviceUnitZone) =>
            PartialView("_ZoneDetails", await BuildZoneViewAsync(idDeviceUnitZone));

        private async Task<ZoneViewModel> BuildZoneViewAsync(int idDeviceUnitZone)
        {
            DeviceUnitZoneDashboard dashboard = await api.DeviceUnitZoneDashboardGet(idDeviceUnitZone);

            // LastSeenAt is stored/served in UTC; convert here for display only.
            IList<DeviceFleetStatus> fleet = (await api.DeviceFleetGet())
                .Where(f => f.DeviceUnitZoneID == idDeviceUnitZone)
                .ToList();
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            foreach (var d in fleet)
            {
                if (d.LastSeenAt is DateTime utc)
                {
                    d.LastSeenAt = TimeZoneHelper.ToUserLocalTime(utc, timeZone);
                }
            }

            bool hasController = dashboard.Devices.Any(d => d.DeviceControllerEnabled == true);
            DeviceUnitZone? zone = null;
            IList<DeviceUnitZoneRule> rules = [];
            if (hasController)
            {
                zone = await api.DeviceUnitZoneGetById(idDeviceUnitZone);
                rules = await api.DeviceUnitZoneRulesGet(idDeviceUnitZone);
            }

            return new ZoneViewModel
            {
                Dashboard = dashboard,
                Fleet = fleet,
                DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
                Zone = zone,
                Rules = rules,
            };
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitAdd(string deviceUnitName)
        {
            DeviceUnit unit = await api.DeviceUnitAdd(new DeviceUnit { DeviceUnitName = deviceUnitName });
            DeviceUnitZone zone = await api.DeviceUnitZoneAdd(new DeviceUnitZone { DeviceUnitID = unit.IDDeviceUnit!.Value, DeviceUnitZoneName = "Default" });
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone = zone.IDDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitDelete(int idDeviceUnit)
        {
            await api.DeviceUnitDelete(idDeviceUnit);
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitRename(int idDeviceUnit, string deviceUnitName)
        {
            await api.DeviceUnitUpdate(new DeviceUnit { IDDeviceUnit = idDeviceUnit, DeviceUnitName = deviceUnitName });
            return RedirectToAction(nameof(Zones), new { idDeviceUnit });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ZoneAdd(int idDeviceUnit, string deviceUnitZoneName)
        {
            await api.DeviceUnitZoneAdd(new DeviceUnitZone { DeviceUnitID = idDeviceUnit, DeviceUnitZoneName = deviceUnitZoneName });
            return RedirectToAction(nameof(Zones), new { idDeviceUnit });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ZoneDelete(int idDeviceUnitZone, int idDeviceUnit)
        {
            await api.DeviceUnitZoneDelete(idDeviceUnitZone);
            return RedirectToAction(nameof(Zones), new { idDeviceUnit });
        }

        // Fetch-then-patch: the update call overwrites every field unconditionally, so posting just the name would blank the other fields.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ZoneRename(int idDeviceUnitZone, string deviceUnitZoneName)
        {
            DeviceUnitZone zone = await api.DeviceUnitZoneGetById(idDeviceUnitZone);
            zone.DeviceUnitZoneName = deviceUnitZoneName;
            await api.DeviceUnitZoneUpdate(zone);
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SafetyLimitsUpdate(int idDeviceUnitZone, int? waterPumpMaxRunSeconds, int? waterPumpCooldownSeconds, bool skipWaterPumpWhenRainPredicted)
        {
            DeviceUnitZone zone = await api.DeviceUnitZoneGetById(idDeviceUnitZone);
            zone.WaterPumpMaxRunSeconds = waterPumpMaxRunSeconds;
            zone.WaterPumpCooldownSeconds = waterPumpCooldownSeconds;
            zone.SkipWaterPumpWhenRainPredicted = skipWaterPumpWhenRainPredicted;
            try
            {
                await api.DeviceUnitZoneUpdate(zone);
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RuleAdd(int idDeviceUnitZone, RelayFunction relayFunction, ConditionType conditionType,
            double? threshold, double? hysteresis, int? interval, int? intervalLength, int? daysOfWeek, int? start, int? duration)
        {
            // Must use ConditionConfigJson.Options here - the options-less JsonSerializer overloads would leak PascalCase onto the wire.
            System.Text.Json.Nodes.JsonNode? config = conditionType switch
            {
                ConditionType.Threshold => System.Text.Json.JsonSerializer.SerializeToNode(new ThresholdConditionConfig(threshold ?? 0, hysteresis ?? 0), ConditionConfigJson.Options),
                ConditionType.Interval => System.Text.Json.JsonSerializer.SerializeToNode(new IntervalConditionConfig(interval ?? 0, intervalLength ?? 0), ConditionConfigJson.Options),
                ConditionType.Schedule => System.Text.Json.JsonSerializer.SerializeToNode(new ScheduleConditionConfig(daysOfWeek ?? 0, start ?? 0, duration ?? 0), ConditionConfigJson.Options),
                _ => null,
            };
            try
            {
                await api.DeviceUnitZoneRuleAdd(new DeviceUnitZoneRule
                {
                    DeviceUnitZoneID = idDeviceUnitZone,
                    RelayFunction = relayFunction,
                    ConditionType = conditionType,
                    ConditionConfig = config,
                });
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RuleDelete(int idDeviceUnitZoneRule, int idDeviceUnitZone)
        {
            await api.DeviceUnitZoneRuleDelete(idDeviceUnitZoneRule);
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> AssignPicker(int idDeviceUnitZone, bool controllerCapable) =>
            View(new AssignPickerViewModel
            {
                IDDeviceUnitZone = idDeviceUnitZone,
                ControllerCapable = controllerCapable,
                Devices = await api.DeviceUnassignedGet(controllerCapable),
            });

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Assign(int idDevice, int idDeviceUnitZone, bool controllerCapable)
        {
            try
            {
                await api.DeviceAssign(new DeviceZoneAssignment { IDDevice = idDevice, IDDeviceUnitZone = idDeviceUnitZone });
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(nameof(AssignPicker), new AssignPickerViewModel
                {
                    IDDeviceUnitZone = idDeviceUnitZone,
                    ControllerCapable = controllerCapable,
                    Devices = await api.DeviceUnassignedGet(controllerCapable),
                });
            }

            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Remove(int idDevice, int idDeviceUnitZone)
        {
            await api.DeviceUnassign(idDevice);
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        // returnUrl comes from window.location client-side (_ZoneStatusBadge) since Request.Path server-side would be the AJAX poll endpoint, not the visible page; falls back to Index if missing/unsafe.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AcknowledgeAlert(int idEventDevice, string? returnUrl)
        {
            await api.DeviceEventAcknowledge(idEventDevice);
            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
        }
    }
}
