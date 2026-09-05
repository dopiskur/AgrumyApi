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
    public class DeviceController(IApi api) : Controller
    {
        public async Task<ActionResult> Fleet()
        {
            IList<DeviceUnit> units = await api.DeviceUnitsGet();
            var zones = new List<DeviceUnitZone>();
            foreach (var unit in units)
            {
                zones.AddRange(await api.DeviceUnitZonesGet(unit.IDDeviceUnit));
            }

            return View(new FleetViewModel
            {
                Devices = await GetFleetForDisplayAsync(),
                Units = units,
                Zones = zones,
            });
        }

        public async Task<ActionResult> FleetRows() => PartialView("_FleetRows", await GetFleetForDisplayAsync());

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AssignToZone(int idDevice, int idDeviceUnitZone)
        {
            try
            {
                await api.DeviceAssign(new DeviceZoneAssignment { IDDevice = idDevice, IDDeviceUnitZone = idDeviceUnitZone });
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Fleet));
        }

        private async Task<IList<DeviceFleetStatus>> GetFleetForDisplayAsync()
        {
            IList<DeviceFleetStatus> fleet = await api.DeviceFleetGet();

            // LastSeenAt is stored/served in UTC; convert here for display only.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            foreach (var d in fleet)
            {
                if (d.LastSeenAt is DateTime utc)
                {
                    d.LastSeenAt = TimeZoneHelper.ToUserLocalTime(utc, timeZone);
                }
            }
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return fleet;
        }

        public async Task<ActionResult> AddDevice()
        {
            User self = await api.UserGetSelf();
            bool stillValid = !string.IsNullOrEmpty(self.DevicePin) &&
                self.DevicePinExpires is DateTime expires && expires > DateTime.UtcNow;

            return View(stillValid
                ? new AddDeviceViewModel { DevicePin = self.DevicePin, ExpiresAt = self.DevicePinExpires }
                : await GenerateNewPinAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RegenerateDevicePin()
        {
            await GenerateNewPinAsync();
            return RedirectToAction(nameof(AddDevice));
        }

        private async Task<AddDeviceViewModel> GenerateNewPinAsync()
        {
            DevicePinResult pin = await api.DevicePinGenerate();
            return new AddDeviceViewModel { DevicePin = pin.DevicePin, ExpiresAt = pin.ExpiresAt };
        }

        public async Task<ActionResult> Details(int? idDevice)
        {
            Device device = await api.DeviceGet(idDevice);

            IList<DeviceFleetStatus> fleet = await api.DeviceFleetGet();
            DeviceFleetStatus? status = fleet.FirstOrDefault(f => f.IDDevice == idDevice);
            ViewBag.FreeHeapBytes = status?.FreeHeapBytes;
            ViewBag.ControllerCapable = status?.ControllerCapable ?? true;
            ViewBag.Kit = status?.Kit;

            ViewBag.Firmware = new DeviceFirmwareViewModel
            {
                IdDevice = idDevice!.Value,
                Board = status?.Board,
                RunningVersion = status?.FirmwareVersion,
                LatestVersion = status?.LatestFirmwareVersion,
                UpdateAvailable = status?.FirmwareUpdateAvailable == true,
                UpdatePending = status?.FirmwareUpdatePending == true,
                TargetVersion = status?.FirmwareTargetVersion,
                Versions = status?.Board is { Length: > 0 } board ? await api.FirmwareList(board) : [],
            };

            return View(device);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> FirmwareUpdate(int idDevice, string? version, string? returnUrl)
        {
            try
            {
                await api.DeviceFirmwareUpdate(new DeviceFirmwareUpdateRequest { IdDevice = idDevice, Version = string.IsNullOrWhiteSpace(version) ? null : version });
                TempData["Message"] = string.IsNullOrWhiteSpace(version) ? "Update to the latest firmware requested." : $"Install of firmware {version} requested.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Details), new { idDevice });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> FirmwareUpdateCancel(int idDevice)
        {
            await api.DeviceFirmwareUpdateCancel(idDevice);
            return RedirectToAction(nameof(Details), new { idDevice });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> Edit(int? idDevice) =>
            View(new DeviceView
            {
                DeviceType = await api.DeviceTypeGet(),
                DeviceTypeService = await api.DeviceTypeServiceGet(),
                Device = await api.DeviceGet(idDevice),
            });

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(DeviceView deviceView)
        {
            if (!ModelState.IsValid)
            {
                deviceView.DeviceType = await api.DeviceTypeGet();
                deviceView.DeviceTypeService = await api.DeviceTypeServiceGet();
                return View(deviceView);
            }

            var device = deviceView.Device!;
            (device.DeviceSensorEnabled, device.DeviceControllerEnabled) = device.DeviceTypeID switch
            {
                0 => (false, false),
                1 => (true, false),
                2 => (false, true),
                3 => (true, true),
                _ => (device.DeviceSensorEnabled, device.DeviceControllerEnabled),
            };

            await api.DeviceUpdate(device);
            // PRG: redirect so a refresh re-fetches Details instead of re-submitting the update.
            return RedirectToAction(nameof(Details), new { idDevice = device.IDDevice });
        }

        public async Task<ActionResult> Events(int? idDevice) => View(new DeviceView
        {
            Device = await api.DeviceGet(idDevice),
            Events = await GetEventsForDisplayAsync(idDevice),
        });

        public async Task<ActionResult> EventsRows(int? idDevice) =>
            PartialView("_EventsRows", await GetEventsForDisplayAsync(idDevice));

        private async Task<IList<DeviceEvent>?> GetEventsForDisplayAsync(int? idDevice)
        {
            IList<DeviceEvent>? events = await api.DeviceEventsGet(idDevice);

            // CreatedAt is stored/served in UTC; convert here for display only.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            foreach (var e in events ?? [])
            {
                if (e.CreatedAt is DateTime utc)
                {
                    e.CreatedAt = TimeZoneHelper.ToUserLocalTime(utc, timeZone);
                }
            }
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return events;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> IssueCommand(CommandTargetType targetType, int targetId, CommandActionType actionType)
        {
            try
            {
                await api.DeviceCommandIssue(new IssueCommandRequest { TargetType = targetType, TargetId = targetId, ActionType = actionType });
                return Ok();
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, ex.Body);
            }
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> Delete(int? idDevice) =>
            View(await api.DeviceGet(idDevice));

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idDevice)
        {
            await api.DeviceDelete(idDevice);
            return RedirectToAction(nameof(Fleet));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> EditSensor(int? idDevice)
        {
            var device = await api.DeviceGet(idDevice);
            return View(new DeviceView
            {
                Device = device,
                DeviceConfigSensor = await api.DeviceConfigSensorGet(device.DeviceConfigSensorID),
                DeviceTypeSensor = await api.DeviceTypeSensorGet(),
            });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditSensor(DeviceView deviceView)
        {
            if (!ModelState.IsValid)
            {
                deviceView.Device = await api.DeviceGet(deviceView.Device!.IDDevice);
                deviceView.DeviceTypeSensor = await api.DeviceTypeSensorGet();
                return View(deviceView);
            }

            await api.DeviceConfigSensorUpdate(new DeviceUpdate
            {
                Device = deviceView.Device,
                Sensor = deviceView.DeviceConfigSensor,
            });
            return RedirectToAction(nameof(Details), new { idDevice = deviceView.Device!.IDDevice });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> EditController(int? idDevice)
        {
            var device = await api.DeviceGet(idDevice);
            return View(new DeviceView
            {
                Device = device,
                DeviceConfigController = await api.DeviceConfigControllerGet(device.DeviceConfigControllerID),
                DeviceTypeRelay = await api.DeviceTypeRelayGet(),
            });
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditController(DeviceView deviceView)
        {
            if (!ModelState.IsValid)
            {
                deviceView.Device = await api.DeviceGet(deviceView.Device!.IDDevice);
                deviceView.DeviceTypeRelay = await api.DeviceTypeRelayGet();
                return View(deviceView);
            }

            try
            {
                await api.DeviceConfigControllerUpdate(new DeviceUpdate
                {
                    Device = deviceView.Device,
                    Controller = deviceView.DeviceConfigController,
                });
            }
            catch (ApiException ex)
            {
                // Empty key: message already names the failing schedule group; asp-validation-summary="ModelOnly" renders it.
                ModelState.AddModelError(string.Empty, ex.Body);
                deviceView.Device = await api.DeviceGet(deviceView.Device!.IDDevice);
                deviceView.DeviceTypeRelay = await api.DeviceTypeRelayGet();
                return View(deviceView);
            }

            return RedirectToAction(nameof(Details), new { idDevice = deviceView.Device!.IDDevice });
        }
    }
}
