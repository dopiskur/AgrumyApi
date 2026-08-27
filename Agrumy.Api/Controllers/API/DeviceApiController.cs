using api.Dal;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Net.Http.Headers;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace api.Controllers.API
{
    //[Route("api/[controller]")]
    [Route("/api/Device")]
    [ApiController]

    public class DeviceApiController : ControllerBase
    {
        private const string GenericError = "An unexpected error occurred. Please try again later.";

        private readonly ILogger<DeviceApiController> _logger;

        public DeviceApiController(ILogger<DeviceApiController> logger)
        {
            _logger = logger;
        }

        #region websvc api
        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<Device>> DevicesGet()
        { //0 day, 1 month, 2 year

            IEnumerable<Device> devices = new List<Device>();
            devices = await RepoFactory.GetRepo().DevicesGetAsync(0);

            return Ok(devices);
        }

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<Device>> DeviceGet(int? idDevice)
        { //0 day, 1 month, 2 year

            Device device = await RepoFactory.GetRepo().DeviceGetAsync(0, idDevice, null, null);
            return Ok(device);
        }

        [Authorize(Roles = "admin")]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUpdate([FromBody] Device device)
        {

            try
            {
                await RepoFactory.GetRepo().DeviceUpdateAsync(device);

                // Updating configversion cache on update
                DeviceCache deviceCache = new DeviceCache();
                deviceCache.ConfigVersion = device.ConfigVersion;
                RepoFactory.GetCache().SetItem(device.ApiId, deviceCache);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceUpdate failed for device {ApiId}", device?.ApiId);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [Authorize(Roles = "admin")]
        [HttpDelete]
        public async Task<ActionResult<bool>> DeviceDelete(int? idDevice)
        {

            try
            {
                await RepoFactory.GetRepo().DeviceDeleteAsync(idDevice);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceDelete failed for device {IdDevice}", idDevice);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [Authorize]
        [HttpGet("Sensor")]
        public async Task<ActionResult<Device>> DeviceConfigSensorGet(int? deviceConfigSensorID)
        { //0 day, 1 month, 2 year

            DeviceConfigSensor deviceConfigSensor = await RepoFactory.GetRepo().DeviceConfigSensorGetAsync(deviceConfigSensorID);
            return Ok(deviceConfigSensor);
        }

        [Authorize]
        [HttpGet("Controller")]
        public async Task<ActionResult<Device>> DeviceConfigControllerGet(int? deviceConfigControllerID)
        { //0 day, 1 month, 2 year

            DeviceConfigController deviceConfigController = await RepoFactory.GetRepo().DeviceConfigControllerGetAsync(deviceConfigControllerID);
            return Ok(deviceConfigController);
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Sensor")]
        public async Task<ActionResult<bool>> DeviceConfigSensorUpdate(DeviceUpdate? deviceUpdate)
        { //0 day, 1 month, 2 year

            await RepoFactory.GetRepo().DeviceConfigSensorUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Sensor);

            DeviceCache deviceCache = new DeviceCache();
            deviceCache.ConfigVersion = deviceUpdate.Device.ConfigVersion;
            RepoFactory.GetCache().SetItem(deviceUpdate.Device.ApiId, deviceCache);
            return true;
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Controller")]
        public async Task<ActionResult<bool>> DeviceConfigControllerUpdate(DeviceUpdate? deviceUpdate)
        { //0 day, 1 month, 2 year

            await RepoFactory.GetRepo().DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);

            DeviceCache deviceCache = new DeviceCache();
            deviceCache.ConfigVersion = deviceUpdate.Device.ConfigVersion;
            RepoFactory.GetCache().SetItem(deviceUpdate.Device.ApiId, deviceCache);
            return true;
        }



        #endregion


        #region Device communication

        // Device point
        [HttpPost("Config")]
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceConfig>> GetConfig([FromBody] Device value)
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

                    Device device = await RepoFactory.GetRepo().DeviceGetAsync(0, null, apiId.ToString(), null);

                    DeviceConfig deviceConfig = await BuildDeviceConfigAsync(device);



                    return Ok(deviceConfig);
                }

                return Ok();     // DEVICE DO NOTHING

            }
            return StatusCode(401);
        }

        // POST api/<DeviceController>

        [HttpPost("Register")]
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceConfig>> DeviceRegistration([FromBody] DeviceRegistration value)
        {
            try
            {

                Device device = new Device();
                User user = new User();

                DeviceConfigSensor deviceConfigSensor = new DeviceConfigSensor();
                DeviceConfigController deviceConfigController = new DeviceConfigController();


                user = await RepoFactory.GetRepo().UserGetAsync(null, value.Email, null);

                if (user.DevicePin != value.DevicePin)
                {
                    return StatusCode(401, "Wrong pin");
                }


                device = await RepoFactory.GetRepo().DeviceGetAsync(user.TenantID, null, null, value.MacAddress);

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

                    await RepoFactory.GetRepo().DeviceAddAsync(device);
                    device = await RepoFactory.GetRepo().DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
                }

                DeviceConfig deviceConfig = await BuildDeviceConfigAsync(device);


                return Ok(deviceConfig);

            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceRegistration failed");
                return StatusCode(500, GenericError);
            }



        }

        [HttpGet]
        private async Task<DeviceConfig> BuildDeviceConfigAsync(Device device)
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
                deviceConfig.DeviceConfigSensor = await RepoFactory.GetRepo().DeviceConfigSensorGetAsync(device.DeviceConfigSensorID);

            }

            if (deviceConfig.DeviceControllerEnabled == true)
            {
                deviceConfig.DeviceConfigController = await RepoFactory.GetRepo().DeviceConfigControllerGetAsync(device.DeviceConfigControllerID);
            }

            return deviceConfig;

        }



        // Device point
        [HttpPost("Authenticate")] // Request authentication
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceAuthentication>> ReqAuth() // private su metode koje se koriste interno, ali i dalaje mora imat httpget
        {
            try
            {
                if (AuthenticationHeaderValue.TryParse(Request.Headers["apiId"], out var apiId) && AuthenticationHeaderValue.TryParse(Request.Headers["apiKey"], out var apiKey))
                {



                    if (await DeviceAuthenticationProvider.VerifyDeviceAsync(apiId, apiKey))
                    {
                        Device device = await RepoFactory.GetRepo().DeviceGetAsync(0, null, apiId.ToString(), null); // Query for configVerion
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
                _logger.LogError(e, "ReqAuth failed");
                return Ok("Authentication error");
            }
        }

        // Check apiKey true/false
        [HttpGet]
        public static bool GetAuth(string apiId, string authKey) // private su metode koje se koriste interno, ali i dalje mora imat httpget
        {
            // in-memory cache lookup only - stays synchronous

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
        public async Task<ActionResult<string>> DeviceTypeGet()
        {
            IEnumerable<DeviceType> deviceType = await RepoFactory.GetRepo().DeviceTypeGetAsync();

            return Ok(deviceType);
        }

        [HttpGet("TypeService")]
        [Authorize]
        public async Task<ActionResult<string>> DeviceTypeServiceGet()
        {

            IEnumerable<DeviceTypeService> deviceTypeService = await RepoFactory.GetRepo().DeviceTypeServiceGetAsync();

            return Ok(deviceTypeService);
        }

        [HttpGet("TypeRelay")]
        [Authorize]
        public async Task<ActionResult<string>> DeviceTypeRelayGet()
        {

            IEnumerable<DeviceTypeRelay> deviceTypeRelay = await RepoFactory.GetRepo().DeviceTypeRelayGetAsync();

            return Ok(deviceTypeRelay);
        }

        [HttpGet("TypeSensor")]
        [Authorize]
        public async Task<ActionResult<string>> DeviceTypeSensorGet()
        {

            IEnumerable<DeviceTypeSensor> deviceTypeSensor = await RepoFactory.GetRepo().DeviceTypeSensorGetAsync();

            return Ok(deviceTypeSensor);
        }

        #endregion
    }
}
