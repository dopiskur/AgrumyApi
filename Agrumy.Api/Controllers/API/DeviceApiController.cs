using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    [Route("/api/Device")]
    public class DeviceApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        #region websvc api

        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<IEnumerable<Device>>> DevicesGet() =>
            Ok(await Repo.DevicesGetAsync(CallerTenantId));

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<Device>> DeviceGet(int? idDevice) =>
            Ok(await Repo.DeviceGetAsync(CallerTenantId, idDevice, null, null));

        [Authorize(Roles = "admin")]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUpdate([FromBody] Device device)
        {
            Device existing = await Repo.DeviceGetByIdAsync(device.IDDevice);
            if (existing.IDDevice == null)
            {
                return NotFound();
            }
            if (existing.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            device.TenantID = existing.TenantID; // payload cannot move a device to another tenant

            await Repo.DeviceUpdateAsync(device);
            await RefreshConfigVersionCacheAsync(device.IDDevice);
            return true;
        }

        [Authorize(Roles = "admin")]
        [HttpDelete]
        public async Task<ActionResult<bool>> DeviceDelete(int? idDevice)
        {
            Device existing = await Repo.DeviceGetByIdAsync(idDevice);
            if (existing.IDDevice == null)
            {
                return NotFound();
            }

            int? callerTenantId = CallerTenantId;
            if (existing.TenantID != callerTenantId)
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await Repo.DeviceDeleteAsync(idDevice, callerTenantId);
            return true;
        }

        [Authorize]
        [HttpGet("Sensor")]
        public async Task<ActionResult<DeviceConfigSensor>> DeviceConfigSensorGet(int? deviceConfigSensorID)
        {
            Device owner = await Repo.DeviceGetByDeviceConfigSensorIdAsync(deviceConfigSensorID);
            if (owner.IDDevice == null)
            {
                return NotFound();
            }
            if (owner.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Sensor config belongs to a different tenant");
            }

            return Ok(await Repo.DeviceConfigSensorGetAsync(deviceConfigSensorID));
        }

        [Authorize]
        [HttpGet("Controller")]
        public async Task<ActionResult<DeviceConfigController>> DeviceConfigControllerGet(int? deviceConfigControllerID)
        {
            Device owner = await Repo.DeviceGetByDeviceConfigControllerIdAsync(deviceConfigControllerID);
            if (owner.IDDevice == null)
            {
                return NotFound();
            }
            if (owner.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Controller config belongs to a different tenant");
            }

            return Ok(await Repo.DeviceConfigControllerGetAsync(deviceConfigControllerID));
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Sensor")]
        public async Task<ActionResult<bool>> DeviceConfigSensorUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            Device existing = await Repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice);
            if (existing.IDDevice == null)
            {
                return NotFound();
            }
            if (existing.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await Repo.DeviceConfigSensorUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Sensor);
            await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);
            return true;
        }

        [Authorize(Roles = "admin")]
        [HttpPut("Controller")]
        public async Task<ActionResult<bool>> DeviceConfigControllerUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            Device existing = await Repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice);
            if (existing.IDDevice == null)
            {
                return NotFound();
            }
            if (existing.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await Repo.DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);
            await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);
            return true;
        }

        /// <summary>
        /// Re-reads the device and writes its authoritative ConfigVersion into the cache, so the
        /// next config poll from that device sees the bump the update just made in the database.
        /// Preserves the device's current apiAuth session token.
        /// </summary>
        private async Task RefreshConfigVersionCacheAsync(int? idDevice)
        {
            Device updated = await Repo.DeviceGetByIdAsync(idDevice);
            if (updated.ApiId == null)
            {
                return;
            }

            DeviceCache entry = Cache.GetDeviceCache(updated.ApiId) ?? new DeviceCache();
            entry.ConfigVersion = updated.ConfigVersion;
            Cache.SetItem(updated.ApiId, entry);
        }

        #endregion


        #region Device communication

        [HttpPost("Config")]
        [EnableRateLimiting("device-auth")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<DeviceConfig>> GetConfig([FromBody] Device value)
        {
            string apiId = HttpContext.DeviceApiId()!;

            DeviceCache? deviceCache = Cache.GetDeviceCache(apiId);
            if (value.ConfigVersion == deviceCache.ConfigVersion)
            {
                return Ok(); // device is up to date - do nothing
            }

            Device device = await Repo.DeviceGetByApiIdAsync(apiId);
            return Ok(await BuildDeviceConfigAsync(device));
        }

        [HttpPost("Register")]
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceConfig>> DeviceRegistration([FromBody] DeviceRegistration value)
        {
            User user = await Repo.UserGetAsync(null, value.Email, null);
            if (user.DevicePin != value.DevicePin)
            {
                return StatusCode(401, "Wrong pin");
            }

            Device device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
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

                await Repo.DeviceAddAsync(device);
                device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
            }

            return Ok(await BuildDeviceConfigAsync(device));
        }

        private async Task<DeviceConfig> BuildDeviceConfigAsync(Device device)
        {
            var deviceConfig = new DeviceConfig
            {
                ConfigVersion = device.ConfigVersion,
                TenantID = device.TenantID,
                deviceID = device.IDDevice,
                DeviceUnitID = device.DeviceUnitID,
                DeviceUnitZoneID = device.DeviceUnitZoneID,
                ApiId = device.ApiId,
                ApiKey = device.ApiKey,
                ServicePoint = device.ServicePoint,
                DeviceTypeServiceID = device.DeviceTypeServiceID,
                ServicePublicKey = device.ServicePublicKey,
                DeviceSensorEnabled = device.DeviceSensorEnabled,
                DeviceControllerEnabled = device.DeviceControllerEnabled,
                BatteryEnabled = device.BatteryEnabled,
                Debug = device.Debug,
                Reboot = device.Reboot,
                Reset = device.Reset,
                FirmwareUpdate = device.FirmwareUpdate,
                Enabled = device.Enabled,
            };

            // Roadmap #3 (OTA). Only look up a build when the flag is set; the firmware does a
            // version comparison of its own so this being present on every Config sync is fine.
            // Harmless on Register: a freshly-created device has FirmwareUpdate == null.
            if (device.FirmwareUpdate == true && device.DeviceTypeID != null)
            {
                DeviceFirmware? firmware = await Repo.DeviceFirmwareLatestGetAsync(device.DeviceTypeID);
                if (firmware != null)
                {
                    deviceConfig.FirmwareVersion = firmware.Version;
                    deviceConfig.FirmwareUrl = firmware.Url;
                }
            }

            if (deviceConfig.DeviceSensorEnabled == true)
            {
                deviceConfig.DeviceConfigSensor = await Repo.DeviceConfigSensorGetAsync(device.DeviceConfigSensorID);
            }
            if (deviceConfig.DeviceControllerEnabled == true)
            {
                deviceConfig.DeviceConfigController = await Repo.DeviceConfigControllerGetAsync(device.DeviceConfigControllerID);
            }

            return deviceConfig;
        }

        [HttpPost("Authenticate")]
        [EnableRateLimiting("device-auth")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<DeviceAuthentication>> ReqAuth()
        {
            string apiId = HttpContext.DeviceApiId()!;
            Device device = await Repo.DeviceGetByApiIdAsync(apiId);

            var deviceAuthentication = new DeviceAuthentication { apiAuth = Guid.NewGuid().ToString() };
            Cache.SetItem(apiId, new DeviceCache
            {
                ConfigVersion = device.ConfigVersion,
                apiAuth = deviceAuthentication.apiAuth,
            });

            return Ok(deviceAuthentication);
        }

        #endregion

        #region Device Types

        [HttpGet("Type")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceType>>> DeviceTypeGet() =>
            Ok(await Repo.DeviceTypeGetAsync());

        [HttpGet("TypeService")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeService>>> DeviceTypeServiceGet() =>
            Ok(await Repo.DeviceTypeServiceGetAsync());

        [HttpGet("TypeRelay")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeRelay>>> DeviceTypeRelayGet() =>
            Ok(await Repo.DeviceTypeRelayGetAsync());

        [HttpGet("TypeSensor")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceTypeSensor>>> DeviceTypeSensorGet() =>
            Ok(await Repo.DeviceTypeSensorGetAsync());

        #endregion
    }
}
