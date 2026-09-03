using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// <summary>Roadmap #81 (hierarchical Unit -> Zone dashboard) and #82 (Unit/Zone CRUD + device
    /// assignment). Complementary to DeviceController.Fleet, not a replacement - Fleet stays the
    /// flat "every device, right now" view; this is physical/logical navigation by growing space.</summary>
    [Authorize]
    public class DeviceUnitController(IApi api) : Controller
    {
        // ---- Dashboard (roadmap #81) --------------------------------------------

        public async Task<ActionResult> Index() => View(await api.DeviceUnitDashboardGet());

        /// <summary>Roadmap #90: polled by Index.cshtml's live-refresh script, same pattern as
        /// DeviceController.FleetRows - built in from the start per the roadmap note, not bolted on.</summary>
        public async Task<ActionResult> IndexCubes() => PartialView("_UnitCubes", await api.DeviceUnitDashboardGet());

        /// <summary>Zone cubes within one unit - auto-enters the single zone directly (skips a
        /// pointless one-cube grid) when the unit has exactly one, per the confirmed #81 design.</summary>
        public async Task<ActionResult> Zones(int idDeviceUnit)
        {
            IList<DeviceUnitZoneDashboard> zones = await api.DeviceUnitZoneDashboardListGet(idDeviceUnit);
            if (zones.Count == 1)
            {
                return RedirectToAction(nameof(Zone), new { idDeviceUnitZone = zones[0].IDDeviceUnitZone });
            }

            return View(new UnitZonesViewModel
            {
                Unit = await api.DeviceUnitGet(idDeviceUnit),
                Zones = zones,
            });
        }

        public async Task<ActionResult> ZonesCubes(int idDeviceUnit) =>
            PartialView("_ZoneCubes", await api.DeviceUnitZoneDashboardListGet(idDeviceUnit));

        /// <summary>Single zone detail - roll-up plus the actual assigned devices (#82: "Zona
        /// prikazuje i detalje - kontroler + senzori"), with the Add Controller/Add Sensor/Remove
        /// controls DeviceManagers roles see. Roadmap #116 rule (5): the device list itself is
        /// Fleet's own rows (Device/_FleetRows.cshtml), filtered to this zone - not a second
        /// hand-built table.</summary>
        public async Task<ActionResult> Zone(int idDeviceUnitZone) => View(await BuildZoneViewAsync(idDeviceUnitZone));

        public async Task<ActionResult> ZoneDetails(int idDeviceUnitZone) =>
            PartialView("_ZoneDetails", await BuildZoneViewAsync(idDeviceUnitZone));

        private async Task<ZoneViewModel> BuildZoneViewAsync(int idDeviceUnitZone)
        {
            DeviceUnitZoneDashboard dashboard = await api.DeviceUnitZoneDashboardGet(idDeviceUnitZone);

            // Roadmap #71 follow-up: LastSeenAt is stored/served in UTC - convert for display only,
            // same as DeviceController.GetFleetForDisplayAsync.
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

            // Roadmap #21: the automation-rules section only appears when the zone has a
            // controller-capable device assigned (existing #82 rule (a)/(b) convention, same test
            // Zone.cshtml already uses) - skip both calls otherwise, nothing to show.
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

        // ---- Unit management (roadmap #82) --------------------------------------

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitAdd(string deviceUnitName)
        {
            DeviceUnit unit = await api.DeviceUnitAdd(new DeviceUnit { DeviceUnitName = deviceUnitName });
            // Roadmap #116 rule (1): auto-create + auto-enter a "Default" zone - same "skip the
            // obvious next step" reasoning as the existing auto-enter-the-only-zone behavior below.
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

        /// <summary>Roadmap #116 rule (2): lightweight inline rename, no separate page.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitRename(int idDeviceUnit, string deviceUnitName)
        {
            await api.DeviceUnitUpdate(new DeviceUnit { IDDeviceUnit = idDeviceUnit, DeviceUnitName = deviceUnitName });
            return RedirectToAction(nameof(Zones), new { idDeviceUnit });
        }

        // ---- Zone management (roadmap #82) --------------------------------------

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

        /// <summary>Roadmap #116 rule (2): lightweight inline rename, no separate page.
        /// Roadmap #21: fetch-then-patch, not a bare partial DTO - DeviceUnitZoneUpdateAsync
        /// overwrites every field unconditionally (it does not merge), so posting just the name
        /// would silently blank WaterPumpMaxRunSeconds/CooldownSeconds back to null on every rename.</summary>
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

        // ---- Automation rules (roadmap #21) --------------------------------------

        /// <summary>Same fetch-then-patch reasoning as ZoneRename above.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SafetyLimitsUpdate(int idDeviceUnitZone, int? waterPumpMaxRunSeconds, int? waterPumpCooldownSeconds)
        {
            DeviceUnitZone zone = await api.DeviceUnitZoneGetById(idDeviceUnitZone);
            zone.WaterPumpMaxRunSeconds = waterPumpMaxRunSeconds;
            zone.WaterPumpCooldownSeconds = waterPumpCooldownSeconds;
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

        /// <summary>One ConditionType's worth of fields arrives per form (the Zone page renders a
        /// separate small "Add Threshold/Interval/Schedule rule" form per function, not one
        /// dynamic JS-driven form) - only the fields matching conditionType are meaningful, same
        /// convention as the Rule wire shape itself. ConditionConfig is built HERE, server-side,
        /// using ConditionConfigJson.Options - never the options-less JsonSerializer overloads,
        /// which would leak PascalCase onto the wire (caught by a contract test while building this
        /// feature, see api.Models.ConditionConfigJson's own remarks).</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RuleAdd(int idDeviceUnitZone, RelayFunction relayFunction, ConditionType conditionType,
            double? threshold, double? hysteresis, int? interval, int? intervalLength, int? daysOfWeek, int? start, int? duration)
        {
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

        // ---- Device assignment (roadmap #82) ------------------------------------

        /// <summary>Roadmap #82 rule (b)/(d): controllerCapable picks which unassigned-device list
        /// (and which button/heading copy) the picker shows - "Add Controller" or "Add Sensor".</summary>
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
                // Roadmap #82 rule (a): the only current source is DeviceUnitApiController's
                // "already has a controller" 409 - same ModelState convention as
                // DeviceController.EditController's ScheduleWindowError handling.
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

        /// <summary>Roadmap #82 rule (e): pure bookkeeping on the API side, no device-facing effect.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Remove(int idDevice, int idDeviceUnitZone)
        {
            await api.DeviceUnassign(idDevice);
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }
    }
}
