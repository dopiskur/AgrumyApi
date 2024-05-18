using api.Dal.Interface;
using api.Models;
using api.Security;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Http.Headers;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace api.Controllers.API
{
    //[Route("api/[controller]")]
    [Route("/api/Device")]
    [ApiController]

    public class DeviceApiController : ControllerBase
    {

        #region websvc api
        [Authorize]
        [HttpGet("All")]
        public ActionResult<Device> DevicesGet()
        { //0 day, 1 month, 2 year

            IEnumerable<Device> devices = new List<Device>();
            devices = RepoFactory.GetRepo().DevicesGet(0);

            return Ok(devices);
        }
        
        [Authorize]
        [HttpGet]
        public ActionResult<Device> DeviceGet(int? idDevice)
        { //0 day, 1 month, 2 year

            Device device = RepoFactory.GetRepo().DeviceGet(0, idDevice, null, null);
            return Ok(device);
        }
        
        [Authorize(Roles = "admin")]
        [HttpPut]
        public ActionResult<bool> DeviceUpdate([FromBody] Device device)
        {

            try
            {
                RepoFactory.GetRepo().DeviceUpdate(device);

                // Updating configversion cache on update
                DeviceCache deviceCache = new DeviceCache();
                deviceCache.ConfigVersion = device.ConfigVersion;
                RepoFactory.GetCache().SetItem(device.ApiId, deviceCache);

                return true;
            }
            catch (Exception e)
            {

                return false;
            }
        }
        
        [Authorize(Roles = "admin")]
        [HttpDelete]
        public ActionResult<bool> DeviceDelete(int? idDevice)
        {

            try
            {
                RepoFactory.GetRepo().DeviceDelete(idDevice);
                return true;
            }
            catch (Exception e)
            {

                return false;
            }
        }

        [Authorize]
        [HttpGet("Sensor")]
        public ActionResult<Device> DeviceConfigSensorGet(int? deviceConfigSensorID)
        { //0 day, 1 month, 2 year

            DeviceConfigSensor deviceConfigSensor = RepoFactory.GetRepo().DeviceConfigSensorGet(deviceConfigSensorID);
            return Ok(deviceConfigSensor);
        }

        [Authorize]
        [HttpGet("Controller")]
        public ActionResult<Device> DeviceConfigControllerGet(int? deviceConfigControllerID)
        { //0 day, 1 month, 2 year

            DeviceConfigController deviceConfigController = RepoFactory.GetRepo().DeviceConfigControllerGet(deviceConfigControllerID);
            return Ok(deviceConfigController);
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Sensor")]
        public ActionResult<bool> DeviceConfigSensorUpdate(DeviceUpdate? deviceUpdate)
        { //0 day, 1 month, 2 year

            RepoFactory.GetRepo().DeviceConfigSensorUpdate(deviceUpdate.Device.IDDevice, deviceUpdate.Sensor);

            DeviceCache deviceCache = new DeviceCache();
            deviceCache.ConfigVersion = deviceUpdate.Device.ConfigVersion;
            RepoFactory.GetCache().SetItem(deviceUpdate.Device.ApiId, deviceCache);
            return true;
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Controller")]
        public ActionResult<bool> DeviceConfigControllerUpdate(DeviceUpdate? deviceUpdate)
        { //0 day, 1 month, 2 year

            RepoFactory.GetRepo().DeviceConfigControllerUpdate(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);

            DeviceCache deviceCache = new DeviceCache();
            deviceCache.ConfigVersion = deviceUpdate.Device.ConfigVersion;
            RepoFactory.GetCache().SetItem(deviceUpdate.Device.ApiId, deviceCache);
            return true;
        }



        #endregion


        #region Device communication

        // Device point
        [HttpPost("Config")]
        public ActionResult<DeviceConfig> GetConfig([FromBody] Device value) // mozdca ovo isto ubacitiu header?
        {
            if (AuthenticationHeaderValue.TryParse(Request.Headers["apiId"], out var apiId) && AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authKey))
            {
                if (!GetAuth(apiId.ToString(), authKey.ToString()))
                {
                    return StatusCode(401);
                }

                Device test = new Device();
                test.ConfigVersion = value.ConfigVersion;

                DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(apiId.ToString());
                if (value.ConfigVersion != deviceCache.ConfigVersion)
                {

                    Device device = RepoFactory.GetRepo().DeviceGet(0, null, apiId.ToString(), null);

                    DeviceConfig deviceConfig = BuildDeviceConfig(device);



                    return Ok(deviceConfig);
                }

                return Ok();     // DEVICE DO NOTHING          

            }
            return StatusCode(401);
        }

        // POST api/<DeviceController>

        [HttpPost("Register")]
        public ActionResult<DeviceConfig> DeviceRegistration([FromBody] DeviceRegistration value)
        {
            try
            {

                Device device = new Device();
                User user = new User();

                DeviceConfigSensor deviceConfigSensor = new DeviceConfigSensor();
                DeviceConfigController deviceConfigController = new DeviceConfigController();


                user = RepoFactory.GetRepo().UserGet(null, value.Email, null);

                if (user.DevicePin != value.DevicePin)
                {
                    return StatusCode(401, "Wrong pin");
                }


                device = RepoFactory.GetRepo().DeviceGet(user.TenantID, null, null, value.MacAddress);

                if (device.IDDevice == null)
                {
                    // Add new device
                    device.ConfigVersion = 1;
                    device.TenantID = user.TenantID;
                    device.DeviceName = "Agrumy_" + value.MacAddress.ToUpper().ToString();
                    device.MacAddress = value.MacAddress;
                    device.ApiId = Guid.NewGuid().ToString();
                    device.ApiKey = Guid.NewGuid().ToString();
                    device.ServicePoint = value.ServicePoint;
                    device.DeviceSensorEnabled = false;
                    device.DeviceControllerEnabled = false;

                    RepoFactory.GetRepo().DeviceAdd(device);
                    device = RepoFactory.GetRepo().DeviceGet(user.TenantID, null, null, value.MacAddress);
                }

                DeviceConfig deviceConfig = BuildDeviceConfig(device);


                return Ok(deviceConfig);

            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }



        }

        [HttpGet]
        private DeviceConfig BuildDeviceConfig(Device device)
        {
            DeviceConfig deviceConfig = new DeviceConfig();

            // return values
            deviceConfig.ConfigVersion = device.ConfigVersion;
            deviceConfig.TenantID = device.TenantID;
            deviceConfig.deviceID = device.IDDevice;
            deviceConfig.DeviceUnitID = device.DeviceUnitID;
            deviceConfig.DeviceUnitZoneID = device.DeviceUnitZoneID;
            deviceConfig.ApiId = device.ApiId;
            deviceConfig.ApiKey = device.ApiKey;
            deviceConfig.ServicePoint = device.ServicePoint;
            deviceConfig.DeviceTypeServiceID = device.DeviceTypeServiceID;
            deviceConfig.ServicePublicKey = device.ServicePublicKey;
            deviceConfig.DeviceSensorEnabled = device.DeviceSensorEnabled;
            deviceConfig.DeviceControllerEnabled = device.DeviceControllerEnabled;
            deviceConfig.BatteryEnabled = device.BatteryEnabled;
            deviceConfig.Debug = device.Debug;
            deviceConfig.Reboot = device.Reboot;
            deviceConfig.Reset = device.Reset;
            deviceConfig.FirmwareUpdate = device.FirmwareUpdate;
            deviceConfig.Enabled = device.Enabled;

            // get config if enabled
            if (deviceConfig.DeviceSensorEnabled == true)
            {
                deviceConfig.DeviceConfigSensor = RepoFactory.GetRepo().DeviceConfigSensorGet(device.DeviceConfigSensorID);

            }

            if (deviceConfig.DeviceControllerEnabled == true)
            {
                deviceConfig.DeviceConfigController = RepoFactory.GetRepo().DeviceConfigControllerGet(device.DeviceConfigControllerID);
            }

            return deviceConfig;

        }



        // Device point
        [HttpPost("Authenticate")] // Request authentication
        public ActionResult<DeviceAuthentication> ReqAuth() // private su metode koje se koriste interno, ali i dalaje mora imat httpget
        {
            try
            {
                if (AuthenticationHeaderValue.TryParse(Request.Headers["apiId"], out var apiId) && AuthenticationHeaderValue.TryParse(Request.Headers["apiKey"], out var apiKey))
                {

                    if (AuthenticationProvider.VerifyDevice(apiId, apiKey))
                    {
                        Device device = RepoFactory.GetRepo().DeviceGet(0, null, apiId.ToString(), null); // Query for configVerion
                        DeviceAuthentication? deviceAuthentication = new DeviceAuthentication();
                        deviceAuthentication.apiAuth = Guid.NewGuid().ToString();
                        DeviceCache deviceCache = new DeviceCache();
                        deviceCache.ConfigVersion = device.ConfigVersion; //Settings config version
                        deviceCache.apiAuth = deviceAuthentication.apiAuth;
                        RepoFactory.GetCache().SetItem(apiId.ToString(), deviceCache);
                        return Ok(deviceAuthentication);
                    }

                    return StatusCode(401);

                }

                return StatusCode(401, "Parameter missing");
            }
            catch (Exception e)
            {

                return Ok(e.Message.ToString());
            }
        }

        // Check apiKey true/false
        [HttpGet]
        public static bool GetAuth(string apiId, string authKey) // private su metode koje se koriste interno, ali i dalje mora imat httpget
        {


            DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(apiId);

            if (authKey.ToString() == deviceCache.apiAuth)
            {
                return true;
            }


            return false;
        }
        #endregion

        #region Device Types

        // Device Types
        [HttpGet("Type")]
        [Authorize]
        public ActionResult<string> DeviceTypeGet()
        {
            IEnumerable<DeviceType> deviceType = RepoFactory.GetRepo().DeviceTypeGet();

            return Ok(deviceType);
        }

        [HttpGet("TypeService")]
        [Authorize]
        public ActionResult<string> DeviceTypeServiceGet()
        {

            IEnumerable<DeviceTypeService> deviceTypeService = RepoFactory.GetRepo().DeviceTypeServiceGet();

            return Ok(deviceTypeService);
        }

        [HttpGet("TypeRelay")]
        [Authorize]
        public ActionResult<string> DeviceTypeRelayGet()
        {

            IEnumerable<DeviceTypeRelay> deviceTypeRelay = RepoFactory.GetRepo().DeviceTypeRelayGet();

            return Ok(deviceTypeRelay);
        }

        [HttpGet("TypeSensor")]
        [Authorize]
        public ActionResult<string> DeviceTypeSensorGet()
        {

            IEnumerable<DeviceTypeSensor> deviceTypeSensor = RepoFactory.GetRepo().DeviceTypeSensorGet();

            return Ok(deviceTypeSensor);
        }

        #endregion
    }
}
