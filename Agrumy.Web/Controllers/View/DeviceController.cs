using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    public class DeviceController : Controller
    {
        // NOTE: removed unused private static DeviceType()/DeviceTypeService()/DeviceTypeRelay()/
        // DeviceTypeSensor() helpers (dead code, never called) - they were the only place the View
        // layer touched RepoFactory.GetRepo() / the DAL directly.

        private readonly IApi _api;

        public DeviceController(IApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

        public async Task<ActionResult> Index()
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }

                IEnumerable<Device> devices = await _api.DevicesGet(jwtKey);

                return View(devices);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Details(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }

                Device device = await _api.DeviceGet(jwtKey, idDevice, null, null); //0 for default tenant

                return View(device);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Edit(int? idDevice)
        {
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            string? roleName;
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            DeviceView? deviceView = new DeviceView();
            deviceView.DeviceType = await _api.DeviceTypeGet(jwtKey);
            deviceView.DeviceTypeService = await _api.DeviceTypeServiceGet(jwtKey);
            deviceView.Device = await _api.DeviceGet(jwtKey, idDevice, null, null);

            return View(deviceView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(DeviceView? deviceView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                Device device = deviceView.Device;
                device.DeviceTypeID = deviceView.Device.DeviceTypeID;

                if (ModelState.IsValid)
                {
                    switch (deviceView.Device.DeviceTypeID)
                    {
                        case 0:
                            device.DeviceSensorEnabled = false;
                            device.DeviceControllerEnabled = false;
                            break;
                        case 1:
                            device.DeviceSensorEnabled = true;
                            device.DeviceControllerEnabled = false;
                            break;
                        case 2:
                            device.DeviceSensorEnabled = false;
                            device.DeviceControllerEnabled = true;
                            break;
                        case 3:
                            device.DeviceSensorEnabled = true;
                            device.DeviceControllerEnabled = true;
                            break;
                        default:
                            break;
                    }

                    await _api.DeviceUpdate(jwtKey, device);
                    // Re-fetch rather than trust the posted model, so ConfigVersion reflects what
                    // the database actually stored (kept in mind for scaling to multiple instances).
                    device = await _api.DeviceGet(jwtKey, device.IDDevice, null, null);
                }

                return View("Details", device);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Delete(int? idDevice)
        {
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            string? roleName;
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            Device device = await _api.DeviceGet(jwtKey, idDevice, null, null);
            return View(device);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                bool result = await _api.DeviceDelete(jwtKey, idDevice);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> EditSensor(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceView? deviceView = new DeviceView();
                deviceView.Device = await _api.DeviceGet(jwtKey, idDevice, null, null);
                deviceView.DeviceConfigSensor = await _api.DeviceConfigSensorGet(jwtKey, deviceView.Device.DeviceConfigControllerID);
                deviceView.DeviceTypeSensor = await _api.DeviceTypeSensorGet(jwtKey);

                return View(deviceView);
            }
            catch (Exception e)
            {
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditSensor(DeviceView? deviceView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceUpdate deviceUpdate = new DeviceUpdate();

                deviceUpdate.Device = deviceView.Device;
                deviceUpdate.Sensor = deviceView.DeviceConfigSensor;

                await _api.DeviceConfigSensorUpdate(jwtKey, deviceUpdate);
                Device device = await _api.DeviceGet(jwtKey, deviceView.Device.IDDevice, null, null);

                return View("Details", device);
            }
            catch
            {
                return View();
            }
        }

        public async Task<ActionResult> EditController(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceView? deviceView = new DeviceView();
                deviceView.Device = await _api.DeviceGet(jwtKey, idDevice, null, null);
                deviceView.DeviceConfigController = await _api.DeviceConfigControllerGet(jwtKey, deviceView.Device.DeviceConfigControllerID);
                deviceView.DeviceTypeRelay = await _api.DeviceTypeRelayGet(jwtKey);

                return View(deviceView);
            }
            catch
            {
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> EditController(DeviceView? deviceView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceUpdate deviceUpdate = new DeviceUpdate();

                deviceUpdate.Device = deviceView.Device;
                deviceUpdate.Controller = deviceView.DeviceConfigController;

                await _api.DeviceConfigControllerUpdate(jwtKey, deviceUpdate);
                Device device = await _api.DeviceGet(jwtKey, deviceView.Device.IDDevice, null, null);

                return View("Details", device);
            }
            catch
            {
                return View();
            }
        }
    }
}
