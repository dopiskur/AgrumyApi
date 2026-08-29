using api.Dal;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;


namespace api.Controllers.API
{
    [Route("/api/Device")]
    [ApiController]
    public class DeviceApiController : ControllerBase
    {
        private readonly ILogger<DeviceApiController> _logger;

        public DeviceApiController(ILogger<DeviceApiController> logger)
        {
            _logger = logger;
        }

        /// <summary>TenantID claim set at login (JwtTokenProvider.CreateToken) - null only if the claim is somehow missing.</summary>
        private int? GetCallerTenantId()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var claim = identity?.FindFirst("TenantID");
            return claim != null && int.TryParse(claim.Value, out var tenantId) ? tenantId : null;
        }

        #region websvc api

        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<Device>> DevicesGet()
        {
            IEnumerable<Device> devices = await RepoFactory.GetRepo().DevicesGetAsync(GetCallerTenantId());
            return Ok(devices);
        }

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<Device>> DeviceGet(int? idDevice)
        {
            Device device = await RepoFactory.GetRepo().DeviceGetAsync(GetCallerTenantId(), idDevice, null, null);
            return Ok(device);
        }

        [Authorize(Roles = "admin")]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUpdate([FromBody] Device device)
        {
            try
            {
                var repo = RepoFactory.GetRepo();

                Device existingDevice = await repo.DeviceGetByIdAsync(device.IDDevice);
                if (existingDevice.IDDevice == null)
                {
                    return NotFound();
                }
                if (existingDevice.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Device belongs to a different tenant");
                }

                device.TenantID = existingDevice.TenantID; // payload cannot move a device to another tenant

                await repo.DeviceUpdateAsync(device);
                await RefreshConfigVersionCacheAsync(device.IDDevice);

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
                Device existingDevice = await RepoFactory.GetRepo().DeviceGetByIdAsync(idDevice);
                if (existingDevice.IDDevice == null)
                {
                    return NotFound();
                }

                int? callerTenantId = GetCallerTenantId();
                if (existingDevice.TenantID != callerTenantId)
                {
                    return StatusCode(403, "Device belongs to a different tenant");
                }

                await RepoFactory.GetRepo().DeviceDeleteAsync(idDevice, callerTenantId);
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
        public async Task<ActionResult<DeviceConfigSensor>> DeviceConfigSensorGet(int? deviceConfigSensorID)
        {
            Device owner = await RepoFactory.GetRepo().DeviceGetByDeviceConfigSensorIdAsync(deviceConfigSensorID);
            if (owner.IDDevice == null)
            {
                return NotFound();
            }
            if (owner.TenantID != GetCallerTenantId())
            {
                return StatusCode(403, "Sensor config belongs to a different tenant");
            }

            return Ok(await RepoFactory.GetRepo().DeviceConfigSensorGetAsync(deviceConfigSensorID));
        }

        [Authorize]
        [HttpGet("Controller")]
        public async Task<ActionResult<DeviceConfigController>> DeviceConfigControllerGet(int? deviceConfigControllerID)
        {
            Device owner = await RepoFactory.GetRepo().DeviceGetByDeviceConfigControllerIdAsync(deviceConfigControllerID);
            if (owner.IDDevice == null)
            {
                return NotFound();
            }
            if (owner.TenantID != GetCallerTenantId())
            {
                return StatusCode(403, "Controller config belongs to a different tenant");
            }

            return Ok(await RepoFactory.GetRepo().DeviceConfigControllerGetAsync(deviceConfigControllerID));
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Sensor")]
        public async Task<ActionResult<bool>> DeviceConfigSensorUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            try
            {
                var repo = RepoFactory.GetRepo();

                Device existingDevice = await repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice);
                if (existingDevice.IDDevice == null)
                {
                    return NotFound();
                }
                if (existingDevice.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Device belongs to a different tenant");
                }

                await repo.DeviceConfigSensorUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Sensor);
                await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceConfigSensorUpdate failed for device {IDDevice}", deviceUpdate.Device.IDDevice);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Controller")]
        public async Task<ActionResult<bool>> DeviceConfigControllerUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            try
            {
                var repo = RepoFactory.GetRepo();

                Device existingDevice = await repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice);
                if (existingDevice.IDDevice == null)
                {
                    return NotFound();
                }
                if (existingDevice.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Device belongs to a different tenant");
                }

                await repo.DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);
                await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceConfigControllerUpdate failed for device {IDDevice}", deviceUpdate.Device.IDDevice);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        /// <summary>
        /// Re-reads the device and writes its authoritative ConfigVersion into the cache, so the
        /// next config poll from that device sees the bump the update just made in the database.
        /// Preserves the device's current apiAuth session token (mutates the existing cache entry
        /// rather than replacing it).
        /// </summary>
        private async Task RefreshConfigVersionCacheAsync(int? idDevice)
        {
            Device updated = await RepoFactory.GetRepo().DeviceGetByIdAsync(idDevice);
            if (updated.ApiId == null)
            {
                return;
            }

            var cache = RepoFactory.GetCache();
            DeviceCache entry = cache.GetDeviceCache(updated.ApiId) ?? new DeviceCache();
            entry.ConfigVersion = updated.ConfigVersion;
            cache.SetItem(updated.ApiId, entry);
        }

        #endregion


        #region Device communication

        [HttpPost("Config")]
        [EnableRateLimiting("device-auth")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<DeviceConfig>> GetConfig([FromBody] Device value)
        {
            string apiId = HttpContext.DeviceApiId()!;

            DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(apiId);
            if (value.ConfigVersion == deviceCache.ConfigVersion)
            {
                return Ok(); // device is up to date - do nothing
            }

            Device device = await RepoFactory.GetRepo().DeviceGetByApiIdAsync(apiId);
            return Ok(await BuildDeviceConfigAsync(device));
        }

        [HttpPost("Register")]
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceConfig>> DeviceRegistration([FromBody] DeviceRegistration value)
        {
            try
            {
                User user = await RepoFactory.GetRepo().UserGetAsync(null, value.Email, null);

                if (user.DevicePin != value.DevicePin)
                {
                    return StatusCode(401, "Wrong pin");
                }

                Device device = await RepoFactory.GetRepo().DeviceGetAsync(user.TenantID, null, null, value.MacAddress);

                if (device.IDDevice == null)
                {
                    device.ConfigVersion = 1;
                    device.TenantID = user.TenantID;
                    device.DeviceName = "Agrumy_" + value.MacAddress.ToUpper();
                    device.MacAddress = value.MacAddress;
                    device.ApiId = Guid.NewGuid().ToString();
                    device.ApiKey = Guid.NewGuid().ToString();
                    device.ServicePoint = value.ServicePoint;
                    device.DeviceSensorEnabled = false;
                    device.DeviceControllerEnabled = false;

                    await RepoFactory.GetRepo().DeviceAddAsync(device);
                    device = await RepoFactory.GetRepo().DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
                }

                return Ok(await BuildDeviceConfigAsync(device));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeviceRegistration failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        private async Task<DeviceConfig> BuildDeviceConfigAsync(Device device)
        {
            DeviceConfig deviceConfig = new DeviceConfig();

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

            // Roadmap #3 (OTA). Only look up a build when the flag is set; the firmware does a
            // version comparison of its own so this being present on every Config sync is fine.
            // Runs for both Register and Config (both call this method) - harmless on Register:
            // a freshly-created device has FirmwareUpdate == null so the branch is skipped.
            if (device.FirmwareUpdate == true && device.DeviceTypeID != null)
            {
                DeviceFirmware? firmware = await RepoFactory.GetRepo().DeviceFirmwareLatestGetAsync(device.DeviceTypeID);
                if (firmware != null)
                {
                    deviceConfig.FirmwareVersion = firmware.Version;
                    deviceConfig.FirmwareUrl = firmware.Url;
                }
            }

            deviceConfig.Enabled = device.Enabled;

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

        [HttpPost("Authenticate")]
        [EnableRateLimiting("device-auth")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<DeviceAuthentication>> ReqAuth()
        {
            try
            {
                string apiId = HttpContext.DeviceApiId()!;
                Device device = await RepoFactory.GetRepo().DeviceGetByApiIdAsync(apiId);

                var deviceAuthentication = new DeviceAuthentication { apiAuth = Guid.NewGuid().ToString() };
                RepoFactory.GetCache().SetItem(apiId, new DeviceCache
                {
                    ConfigVersion = device.ConfigVersion,
                    apiAuth = deviceAuthentication.apiAuth,
                });

                return Ok(deviceAuthentication);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ReqAuth failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        #endregion

        #region Device Types

        [HttpGet("Type")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceType>>> DeviceTypeGet() =>
            Ok(await RepoFactory.GetRepo().DeviceTypeGetAsync());

        [HttpGet("TypeService")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeService>>> DeviceTypeServiceGet() =>
            Ok(await RepoFactory.GetRepo().DeviceTypeServiceGetAsync());

        [HttpGet("TypeRelay")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeRelay>>> DeviceTypeRelayGet() =>
            Ok(await RepoFactory.GetRepo().DeviceTypeRelayGetAsync());

        [HttpGet("TypeSensor")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeSensor>>> DeviceTypeSensorGet() =>
            Ok(await RepoFactory.GetRepo().DeviceTypeSensorGetAsync());

        #endregion
    }
}
