using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize]
    public class DeviceController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.DevicesGet());

        public async Task<ActionResult> Details(int? idDevice) =>
            View(await api.DeviceGet(idDevice));

        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Edit(int? idDevice) =>
            View(new DeviceView
            {
                DeviceType = await api.DeviceTypeGet(),
                DeviceTypeService = await api.DeviceTypeServiceGet(),
                Device = await api.DeviceGet(idDevice),
            });

        [Authorize(Roles = "admin")]
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
            // Re-fetch so ConfigVersion reflects what the database actually stored.
            return View("Details", await api.DeviceGet(device.IDDevice));
        }

        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Events(int? idDevice) =>
            View(new DeviceView
            {
                Device = await api.DeviceGet(idDevice),
                Events = await api.DeviceEventsGet(idDevice),
            });

        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Delete(int? idDevice) =>
            View(await api.DeviceGet(idDevice));

        [Authorize(Roles = "admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idDevice)
        {
            await api.DeviceDelete(idDevice);
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "admin")]
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

        [Authorize(Roles = "admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditSensor(DeviceView deviceView)
        {
            await api.DeviceConfigSensorUpdate(new DeviceUpdate
            {
                Device = deviceView.Device,
                Sensor = deviceView.DeviceConfigSensor,
            });
            return View("Details", await api.DeviceGet(deviceView.Device!.IDDevice));
        }

        [Authorize(Roles = "admin")]
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

        [Authorize(Roles = "admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditController(DeviceView deviceView)
        {
            await api.DeviceConfigControllerUpdate(new DeviceUpdate
            {
                Device = deviceView.Device,
                Controller = deviceView.DeviceConfigController,
            });
            return View("Details", await api.DeviceGet(deviceView.Device!.IDDevice));
        }
    }
}
