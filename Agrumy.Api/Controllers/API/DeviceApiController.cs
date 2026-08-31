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
            Ok(CallerReadsDevicesGlobally ? await Repo.DevicesGetAllAsync() : await Repo.DevicesGetAsync(CallerTenantId));

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<Device>> DeviceGet(int? idDevice)
        {
            // #66 Phase 2: a Global reader/Device/admin sees any tenant's device - DeviceGetAsync's
            // tenant filter would hide it, so use the unfiltered by-id lookup for them.
            Device? device = CallerReadsDevicesGlobally
                ? await Repo.DeviceGetByIdAsync(idDevice)
                : await Repo.DeviceGetAsync(CallerTenantId, idDevice, null, null);
            return device is null ? NotFound() : Ok(device);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUpdate([FromBody] Device device)
        {
            var (existing, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(device.IDDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            device.TenantID = existing!.TenantID; // payload cannot move a device to another tenant

            await Repo.DeviceUpdateAsync(device);
            await RefreshConfigVersionCacheAsync(device.IDDevice);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete]
        public async Task<ActionResult<bool>> DeviceDelete(int? idDevice)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            // The device's OWN tenant, not the caller's - a Global admin/Device deleting a foreign
            // tenant's device would otherwise silently match zero rows.
            await Repo.DeviceDeleteAsync(idDevice, device!.TenantID);
            return true;
        }

        [Authorize]
        [HttpGet("Sensor")]
        public async Task<ActionResult<DeviceConfigSensor>> DeviceConfigSensorGet(int? deviceConfigSensorID)
        {
            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByDeviceConfigSensorIdAsync(deviceConfigSensorID), "Sensor config", forWrite: false);
            if (error != null)
            {
                return error;
            }

            return Ok(await Repo.DeviceConfigSensorGetAsync(deviceConfigSensorID));
        }

        [Authorize]
        [HttpGet("Controller")]
        public async Task<ActionResult<DeviceConfigController>> DeviceConfigControllerGet(int? deviceConfigControllerID)
        {
            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByDeviceConfigControllerIdAsync(deviceConfigControllerID), "Controller config", forWrite: false);
            if (error != null)
            {
                return error;
            }

            return Ok(await Repo.DeviceConfigControllerGetAsync(deviceConfigControllerID));
        }

        /// <summary>Roadmap #28 diagnostic view - #66 Phase 2 opened it from admin-only to any
        /// authenticated caller (a Tenant reader may look at their tenant's event log); tenant
        /// ownership still enforced the same way as every other Device sub-resource GET.</summary>
        [Authorize]
        [HttpGet("Events")]
        public async Task<ActionResult<IList<DeviceEvent>>> DeviceEventsGet(int? idDevice)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: false);
            if (error != null)
            {
                return error;
            }

            // The device's own tenant (== the caller's for a tenant-scoped caller; the ensure call
            // above already authorized a cross-tenant global reader).
            return Ok(await Repo.EventDeviceGetAsync(device!.IDDevice, device.TenantID));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Sensor")]
        public async Task<ActionResult<bool>> DeviceConfigSensorUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            await Repo.DeviceConfigSensorUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Sensor);
            await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Controller")]
        public async Task<ActionResult<bool>> DeviceConfigControllerUpdate(DeviceUpdate? deviceUpdate)
        {
            if (deviceUpdate?.Device?.IDDevice == null)
            {
                return BadRequest("Device is required.");
            }

            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            await Repo.DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);
            await RefreshConfigVersionCacheAsync(deviceUpdate.Device.IDDevice);
            return true;
        }

        /// <summary>
        /// Looks a device up and checks the caller may touch it. Returns (null, 404) when no device
        /// matches, (device, 403) on a tenant mismatch, or (device, null) when access is allowed.
        /// #66 Phase 2: a foreign tenant's device passes for Global roles - CallerManagesDevicesGlobally
        /// on a write, the wider CallerReadsDevicesGlobally (includes Global reader) on a read.
        /// </summary>
        private async Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(
            Func<Task<Device?>> lookup, string ownerLabel, bool forWrite)
        {
            Device? device = await lookup();
            if (device is null)
            {
                return (null, NotFound());
            }

            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (device.TenantID != CallerTenantId && !crossTenantAllowed)
            {
                return (device, StatusCode(403, $"{ownerLabel} belongs to a different tenant"));
            }
            return (device, null);
        }

        /// <summary>
        /// Re-reads the device and writes its authoritative ConfigVersion into the cache, so the
        /// next config poll from that device sees the bump the update just made in the database.
        /// Preserves the device's current apiAuth session token.
        /// </summary>
        private async Task RefreshConfigVersionCacheAsync(int? idDevice)
        {
            Device? updated = await Repo.DeviceGetByIdAsync(idDevice);
            if (updated?.ApiId == null)
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

            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            return device is null ? NotFound() : Ok(await BuildDeviceConfigAsync(device));
        }

        /// <summary>Roadmap #28. No identity field in the body by design - see DeviceEventPush;
        /// deviceID/tenantID come exclusively from the authenticated apiId, same rule as
        /// SensorDataApiController.Post (#47).</summary>
        [HttpPost("Event")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> PushEvent([FromBody] DeviceEventPush value)
        {
            if (!Enum.TryParse<DeviceEventType>(value.EventType, ignoreCase: true, out var eventType))
            {
                return BadRequest($"Unknown eventType: {value.EventType}");
            }

            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.EventDevicePushAsync(device.IDDevice!.Value, device.TenantID ?? 0, eventType, value.Message);
            return Ok();
        }

        [HttpPost("Register")]
        [EnableRateLimiting("device-auth")]
        public async Task<ActionResult<DeviceConfig>> DeviceRegistration([FromBody] DeviceRegistration value)
        {
            if (string.IsNullOrWhiteSpace(value.MacAddress))
            {
                return BadRequest("macAddress is required.");
            }

            // Roadmap #70: expiry and match failures share one generic 401 - a distinct "pin
            // expired" reply would confirm the email exists to an unauthenticated caller.
            User? user = await Repo.UserGetAsync(null, value.Email, null);
            if (user is null || !AuthenticationProvider.VerifyPin(user.DevicePin, user.DevicePinExpires, value.DevicePin))
            {
                return StatusCode(401, "Wrong user or pin");
            }

            Device? device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
            if (device is null)
            {
                await Repo.DeviceAddAsync(new Device
                {
                    ConfigVersion = 1,
                    TenantID = user.TenantID,
                    DeviceName = "Agrumy_" + value.MacAddress.ToUpper(),
                    MacAddress = value.MacAddress,
                    ApiId = Guid.NewGuid().ToString(),
                    ApiKey = Guid.NewGuid().ToString(),
                    ServicePoint = value.ServicePoint,
                    DeviceSensorEnabled = false,
                    DeviceControllerEnabled = false,
                });

                device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
                if (device is null)
                {
                    return StatusCode(500, "Device registration did not persist.");
                }
            }

            // Roadmap #70: the PIN is single-use - consumed here so a leaked/shoulder-surfed one
            // is worthless afterwards; the next device registration needs a fresh PIN from
            // POST /api/User/DevicePin.
            if (user.IDUser is int idUser)
            {
                await Repo.UserSetDevicePinAsync(idUser, null, null);
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
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return NotFound();
            }

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
