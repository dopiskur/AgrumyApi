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

        private static string roleName="";


        public ActionResult Index()
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }


                IEnumerable<Device> devices = RepoFactory.GetApi().DevicesGet(jwtKey).Result;

                return View(devices);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }

        public ActionResult Details(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }

                Device device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result; //0 for default tenant


                return View(device);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }

        }



        public ActionResult Edit(int? idDevice)
        {

            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }


            DeviceView? deviceView = new DeviceView();
            deviceView.DeviceType = RepoFactory.GetApi().DeviceTypeGet(jwtKey).Result;
            deviceView.DeviceTypeService = RepoFactory.GetApi().DeviceTypeServiceGet(jwtKey).Result;

            deviceView.Device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result;

            return View(deviceView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(DeviceView? deviceView)
        {
            try
            {

                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
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

                    RepoFactory.GetApi().DeviceUpdate(jwtKey,device);
                    // Re-fetch rather than trust the posted model, so ConfigVersion reflects what
                    // the database actually stored (kept in mind for scaling to multiple instances).
                    device = RepoFactory.GetApi().DeviceGet(jwtKey, device.IDDevice, null, null).Result;

                }

                return View("Details", device);
            }
            catch (Exception e)
            {
                //return View();
                return StatusCode(500, e.Message);
            }
        }



        public ActionResult Delete(int? idDevice)
        {
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            Device device = RepoFactory.GetApi().DeviceGet(jwtKey,idDevice, null, null).Result;
            return View(device);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirm(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                bool result = RepoFactory.GetApi().DeviceDelete(jwtKey, idDevice).Result;
                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }

        public ActionResult EditSensor(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceView? deviceView = new DeviceView();
                deviceView.Device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result;
                deviceView.DeviceConfigSensor = RepoFactory.GetApi().DeviceConfigSensorGet(jwtKey, deviceView.Device.DeviceConfigControllerID).Result;
                deviceView.DeviceTypeSensor = RepoFactory.GetApi().DeviceTypeSensorGet(jwtKey).Result;


                return View(deviceView);
            }
            catch (Exception e)
            {

                return View();
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditSensor(DeviceView? deviceView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceUpdate deviceUpdate = new DeviceUpdate();

                deviceUpdate.Device = deviceView.Device;
                deviceUpdate.Sensor = deviceView.DeviceConfigSensor;

                RepoFactory.GetApi().DeviceConfigSensorUpdate(jwtKey, deviceUpdate);
                Device device = RepoFactory.GetApi().DeviceGet(jwtKey, deviceView.Device.IDDevice, null, null).Result;

                return View("Details", device);
            }
            catch
            {
                return View();
            }
        }


        public ActionResult EditController(int? idDevice)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceView? deviceView = new DeviceView();
                deviceView.Device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result;
                deviceView.DeviceConfigController = RepoFactory.GetApi().DeviceConfigControllerGet(jwtKey, deviceView.Device.DeviceConfigControllerID).Result;
                deviceView.DeviceTypeRelay = RepoFactory.GetApi().DeviceTypeRelayGet(jwtKey).Result;


                return View(deviceView);
            }
            catch
            {

                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditController(DeviceView? deviceView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                DeviceUpdate deviceUpdate = new DeviceUpdate();

                deviceUpdate.Device = deviceView.Device;
                deviceUpdate.Controller = deviceView.DeviceConfigController;

                RepoFactory.GetApi().DeviceConfigControllerUpdate(jwtKey, deviceUpdate);
                Device device = RepoFactory.GetApi().DeviceGet(jwtKey, deviceView.Device.IDDevice, null, null).Result;

                return View("Details", device);
            }
            catch
            {
                return View();
            }
        }
    }
}
