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
        /// controls DeviceManagers roles see.</summary>
        public async Task<ActionResult> Zone(int idDeviceUnitZone) => View(await api.DeviceUnitZoneDashboardGet(idDeviceUnitZone));

        public async Task<ActionResult> ZoneDetails(int idDeviceUnitZone) =>
            PartialView("_ZoneDetails", await api.DeviceUnitZoneDashboardGet(idDeviceUnitZone));

        // ---- Unit management (roadmap #82) --------------------------------------

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitAdd(string deviceUnitName)
        {
            await api.DeviceUnitAdd(new DeviceUnit { DeviceUnitName = deviceUnitName });
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UnitDelete(int idDeviceUnit)
        {
            await api.DeviceUnitDelete(idDeviceUnit);
            return RedirectToAction(nameof(Index));
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
