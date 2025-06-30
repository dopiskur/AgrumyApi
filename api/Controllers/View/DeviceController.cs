using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    public class DeviceController : Controller
    {
        public static IEnumerable<DeviceType> deviceType = new List<DeviceType>();
        public static IEnumerable<DeviceTypeService> deviceTypeService = new List<DeviceTypeService>();
        public static IEnumerable<DeviceTypeRelay> deviceTypeRelay = new List<DeviceTypeRelay>();
        public static IEnumerable<DeviceTypeSensor> deviceTypeSensor = new List<DeviceTypeSensor>();

        private static IEnumerable<DeviceType> DeviceType() 
        {
            
            if (deviceType == null) // Stavljeno je static da se ucita samo jednom, jer aplikacija ne omogucuje dodavanje rola, nije potrebno. Ali ostavljamo shemu za buducnost.
            {
                deviceType = RepoFactory.GetRepo().DeviceTypeGet();
            }

            return deviceType;
        }
        private static IEnumerable<DeviceTypeService> DeviceTypeService() 
        {
            
            if(deviceTypeService == null)
            {
                deviceTypeService = RepoFactory.GetRepo().DeviceTypeServiceGet();
            }
            
            return deviceTypeService;
        }
        private static IEnumerable<DeviceTypeRelay> DeviceTypeRelay() // Stavljeno je static da se ucita samo jednom, jer aplikacija ne omogucuje dodavanje rola, nije potrebno. Ali ostavljamo shemu za buducnost.
        {
            
            if(deviceTypeRelay == null)
            {
                deviceTypeRelay = RepoFactory.GetRepo().DeviceTypeRelayGet();
            }
            
            return deviceTypeRelay;
        }
        private static IEnumerable<DeviceTypeSensor> DeviceTypeSensor() // Stavljeno je static da se ucita samo jednom, jer aplikacija ne omogucuje dodavanje rola, nije potrebno. Ali ostavljamo shemu za buducnost.
        {
            
            if (deviceTypeSensor == null)
            {
                deviceTypeSensor = RepoFactory.GetRepo().DeviceTypeSensorGet();
            }

            return deviceTypeSensor;
        }

        private static string roleName="";


        // GET: DeviceViewController
        public ActionResult Index()
        {
            //HANDLE SESSION
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

        // GET: DeviceViewController/Details/5
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



        // GET: DeviceViewController/Edit/5
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

        // POST: DeviceViewController/Edit/5
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
                    // change DeviceType
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
                            // code block
                            break;
                    }

                    RepoFactory.GetApi().DeviceUpdate(jwtKey,device);
                    device = RepoFactory.GetApi().DeviceGet(jwtKey, device.IDDevice, null, null).Result; // Sinkroniziram config verziju s bazom u view zbog skaliranja

                }

                return View("Details", device);
            }
            catch (Exception e)
            {
                //return View();
                return StatusCode(500, e.Message);
            }
        }



        // GET: UserViewController1/Delete/5
        public ActionResult Delete(int? idDevice)
        {
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            Device device = RepoFactory.GetApi().DeviceGet(jwtKey,idDevice, null, null).Result;
            return View(device);
        }

        // Post: DELETE
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

        // POST: EditSensor
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


        // POST: EditController
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
