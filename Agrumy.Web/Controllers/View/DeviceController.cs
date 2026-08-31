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
        // Roadmap #8: read-only fleet status - any authenticated caller, same reasoning as Events.
        // Supersedes the old plain device list (DevicesGet is no longer called from the Web) - Fleet
        // shows the same devices plus online/diagnostic state, so there was nothing the old list had left to offer.
        public async Task<ActionResult> Fleet()
        {
            IList<DeviceFleetStatus> fleet = await api.DeviceFleetGet();

            // Roadmap #71 follow-up: LastSeenAt is stored/served in UTC - convert for display only.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            foreach (var d in fleet)
            {
                if (d.LastSeenAt is DateTime utc)
                {
                    d.LastSeenAt = TimeZoneHelper.ToUserLocalTime(utc, timeZone);
                }
            }
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(fleet);
        }

        /// <summary>Roadmap #76: one click gets the caller a PIN ready to type into the device's
        /// setup portal - reuses their still-valid PIN if they have one (roadmap #70: multi-use,
        /// so there is usually one sitting around from the last device) instead of always
        /// rotating, which would invalidate a PIN mid-way through registering several sensors.</summary>
        public async Task<ActionResult> AddDevice()
        {
            User self = await api.UserGetSelf();
            bool stillValid = !string.IsNullOrEmpty(self.DevicePin) &&
                self.DevicePinExpires is DateTime expires && expires > DateTime.UtcNow;

            return View(stillValid
                ? new AddDeviceViewModel { DevicePin = self.DevicePin, ExpiresAt = self.DevicePinExpires }
                : await GenerateNewPinAsync());
        }

        /// <summary>Roadmap #76: the conscious "throw away my current PIN" action - AddDevice's own
        /// GET never rotates a still-valid PIN on its own.</summary>
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

            // Free heap is a per-device diagnostic drill-down, not a fleet-wide health signal - it
            // belongs here, not as a Fleet column. Fleet's own endpoint already computes it; find
            // this one device in the same caller-scoped list rather than adding a single-device API.
            IList<DeviceFleetStatus> fleet = await api.DeviceFleetGet();
            ViewBag.FreeHeapBytes = fleet.FirstOrDefault(f => f.IDDevice == idDevice)?.FreeHeapBytes;

            return View(device);
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

        // #66 Phase 2: events are a read-only diagnostic - any authenticated caller (Tenant
        // reader included); the API scopes to the caller's tenant.
        public async Task<ActionResult> Events(int? idDevice)
        {
            var view = new DeviceView
            {
                Device = await api.DeviceGet(idDevice),
                Events = await api.DeviceEventsGet(idDevice),
            };

            // Roadmap #71 follow-up: CreatedAt is stored/served in UTC - convert for display only.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            foreach (var e in view.Events ?? [])
            {
                if (e.CreatedAt is DateTime utc)
                {
                    e.CreatedAt = TimeZoneHelper.ToUserLocalTime(utc, timeZone);
                }
            }
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(view);
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

            await api.DeviceConfigControllerUpdate(new DeviceUpdate
            {
                Device = deviceView.Device,
                Controller = deviceView.DeviceConfigController,
            });
            return RedirectToAction(nameof(Details), new { idDevice = deviceView.Device!.IDDevice });
        }
    }
}
