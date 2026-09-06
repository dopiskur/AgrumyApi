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

            string? timeZone = User.GetTimeZone();
            return View(new UnitZonesViewModel
            {
                Unit = await api.DeviceUnitGet(idDeviceUnit),
                Zones = zones,
                DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone,
                // Last 24h, hourly buckets - same window _ZoneDetails' sparkline trend already uses.
                SensorDataJson = await api.SensorDataUnitAverageGet(idDeviceUnit, 24, 1),
                DiscoveredDevices = await api.DiscoveryResultsGet(idDeviceUnit, null),
                WifiConfigs = await api.DiscoveryWifiConfigsGet(),
            });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ScanUnit(DiscoveryScanRequest request)
        {
            try
            {
                await api.DiscoveryScan(request);
                TempData["Message"] = "Scan started - discovered devices will appear here shortly.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zones), new { idDeviceUnit = request.UnitID });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RegisterDiscoveredDeviceUnit(DiscoveryRegisterRequest request)
        {
            try
            {
                DiscoveryRegisterResult result = await api.DiscoveryRegister(request);
                var (message, error) = DiscoveryRegisterOutcomeMessage.For(result.Outcome);
                TempData["Message"] = message;
                TempData["Error"] = error;
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zones), new { idDeviceUnit = request.UnitID });
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
            string? timeZone = User.GetTimeZone();
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
                DiscoveredDevices = await api.DiscoveryResultsGet(null, idDeviceUnitZone),
                WifiConfigs = await api.DiscoveryWifiConfigsGet(),
            };
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ScanZone(DiscoveryScanRequest request)
        {
            try
            {
                await api.DiscoveryScan(request);
                TempData["Message"] = "Scan started - discovered devices will appear here shortly.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone = request.ZoneID });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RegisterDiscoveredDeviceZone(DiscoveryRegisterRequest request)
        {
            try
            {
                DiscoveryRegisterResult result = await api.DiscoveryRegister(request);
                var (message, error) = DiscoveryRegisterOutcomeMessage.For(result.Outcome);
                TempData["Message"] = message;
                TempData["Error"] = error;
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone = request.ZoneID });
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

        // Roadmap #234 - all three null together means "no tank tracking", the empty-string->null coercion below keeps a blank form submit from writing a zero-capacity/zero-calibration tank instead.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> TankCalibrationUpdate(int idDeviceUnitZone, double? tankCapacityLiters, int? waterLevelRawEmpty, int? waterLevelRawFull)
        {
            DeviceUnitZone zone = await api.DeviceUnitZoneGetById(idDeviceUnitZone);
            zone.TankCapacityLiters = tankCapacityLiters;
            zone.WaterLevelRawEmpty = waterLevelRawEmpty;
            zone.WaterLevelRawFull = waterLevelRawFull;
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

        // ---- Rules (Zone/Unit/Global scope, roadmap #212) ----------------------------

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RuleAdd(int idDeviceUnitZone, RuleFormInput input)
        {
            await AddRuleAsync(BuildRule(input, idDeviceUnitZone, null), r => api.DeviceUnitZoneRuleAdd(r));
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RuleDelete(int idDeviceUnitZoneRule, int idDeviceUnitZone)
        {
            await DeleteRuleAsync(idDeviceUnitZoneRule, r => api.DeviceUnitZoneRuleDelete(r));
            return RedirectToAction(nameof(Zone), new { idDeviceUnitZone });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> UnitRules(int idDeviceUnit) => View(new RuleEditorViewModel
        {
            Scope = RuleScope.Unit,
            ScopeId = idDeviceUnit,
            Rules = await api.DeviceUnitRulesGet(idDeviceUnit),
            RedirectActionName = nameof(UnitRules),
        });

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitRuleAdd(int idDeviceUnit, RuleFormInput input)
        {
            await AddRuleAsync(BuildRule(input, null, idDeviceUnit), r => api.DeviceUnitRuleAdd(r));
            return RedirectToAction(nameof(UnitRules), new { idDeviceUnit });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitRuleDelete(int idDeviceUnitZoneRule, int idDeviceUnit)
        {
            await DeleteRuleAsync(idDeviceUnitZoneRule, r => api.DeviceUnitRuleDelete(r));
            return RedirectToAction(nameof(UnitRules), new { idDeviceUnit });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> GlobalRules() => View(new RuleEditorViewModel
        {
            Scope = RuleScope.Global,
            Rules = await api.GlobalRulesGet(),
            RedirectActionName = nameof(GlobalRules),
        });

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> GlobalRuleAdd(RuleFormInput input)
        {
            await AddRuleAsync(BuildRule(input, null, null), r => api.GlobalRuleAdd(r));
            return RedirectToAction(nameof(GlobalRules));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> GlobalRuleDelete(int idDeviceUnitZoneRule)
        {
            await DeleteRuleAsync(idDeviceUnitZoneRule, r => api.GlobalRuleDelete(r));
            return RedirectToAction(nameof(GlobalRules));
        }

        private async Task AddRuleAsync(DeviceUnitZoneRule rule, Func<DeviceUnitZoneRule, Task<int>> add)
        {
            try
            {
                await add(rule);
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
        }

        private async Task DeleteRuleAsync(int idRule, Func<int?, Task> delete)
        {
            try
            {
                await delete(idRule);
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
        }

        /// Builds a DeviceUnitZoneRule from the form input - exactly one of idDeviceUnitZone/idDeviceUnit is non-null for Zone/Unit scope, both null for Global.
        private static DeviceUnitZoneRule BuildRule(RuleFormInput input, int? idDeviceUnitZone, int? idDeviceUnit)
        {
            var conditions = new List<RuleCondition>();
            foreach (RuleConditionInput slot in input.Conditions)
            {
                if (ToRuleCondition(slot) is RuleCondition c)
                {
                    conditions.Add(c);
                }
            }
            return new DeviceUnitZoneRule
            {
                DeviceUnitZoneID = idDeviceUnitZone,
                DeviceUnitID = idDeviceUnit,
                ActionType = input.ActionType,
                RelayFunction = input.ActionType == ActionType.Relay ? input.RelayFunction : null,
                SensorMetric = input.ActionType == ActionType.Notification ? input.SensorMetric : null,
                NotificationSubject = input.ActionType == ActionType.Notification ? input.NotificationSubject : null,
                NotificationBody = input.ActionType == ActionType.Notification ? input.NotificationBody : null,
                Conditions = conditions,
            };
        }

        /// Null return means "this slot is unused" - the API itself rejects an empty resulting Conditions list, so an all-empty form still gets a clear error instead of silently saving nothing.
        private static RuleCondition? ToRuleCondition(RuleConditionInput input)
        {
            if (input.ConditionType is not ConditionType type)
            {
                return null;
            }
            // Must use ConditionConfigJson.Options here - the options-less JsonSerializer overloads would leak PascalCase onto the wire.
            System.Text.Json.Nodes.JsonNode? config = type switch
            {
                ConditionType.Threshold => System.Text.Json.JsonSerializer.SerializeToNode(new ThresholdConditionConfig(input.Threshold ?? 0, input.Hysteresis ?? 0), ConditionConfigJson.Options),
                ConditionType.Interval => System.Text.Json.JsonSerializer.SerializeToNode(new IntervalConditionConfig(input.Interval ?? 0, input.IntervalLength ?? 0), ConditionConfigJson.Options),
                ConditionType.Schedule => System.Text.Json.JsonSerializer.SerializeToNode(new ScheduleConditionConfig(input.DaysOfWeek ?? 0, input.Start ?? 0, input.Duration ?? 0), ConditionConfigJson.Options),
                ConditionType.Astronomical => System.Text.Json.JsonSerializer.SerializeToNode(new AstronomicalConditionConfig(input.DaysOfWeek ?? 0, input.SunriseOffsetMinutes ?? 0, input.SunsetOffsetMinutes ?? 0), ConditionConfigJson.Options),
                ConditionType.RuleTriggered => System.Text.Json.JsonSerializer.SerializeToNode(new RuleTriggeredConditionConfig(input.ReferencedRuleId ?? 0), ConditionConfigJson.Options),
                _ => null,
            };
            return new RuleCondition(type, config, input.Operator);
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
