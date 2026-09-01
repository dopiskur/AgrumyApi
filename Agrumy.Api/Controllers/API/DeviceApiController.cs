using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
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
            // tenant's device would otherwise silently match zero rows. Roadmap #108: same ?? 0
            // fallback as #96/#96-follow-ups - a null TenantID device's row was written with 0
            // (DeviceAddAsync's own ?? 0 convention), so filtering on the bare nullable here would
            // delete zero rows instead.
            await Repo.DeviceDeleteAsync(idDevice, device!.TenantID ?? 0);
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

        /// <summary>Roadmap #8 fleet dashboard - read-only status of every device at once, so it is
        /// open to any authenticated caller (same reasoning as DeviceEventsGet); tenant scoping
        /// mirrors DevicesGet, with global readers seeing all tenants.</summary>
        [Authorize]
        [HttpGet("Fleet")]
        public async Task<ActionResult<IList<DeviceFleetStatus>>> DeviceFleetGet() =>
            Ok(await Repo.DeviceFleetGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

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
            // above already authorized a cross-tenant global reader). Same ?? 0 fallback as
            // EventDevicePushAsync's caller - roadmap #96, a null TenantID otherwise never matches
            // the TenantID=0 row the push side actually wrote.
            return Ok(await Repo.EventDeviceGetAsync(device!.IDDevice, device.TenantID ?? 0));
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

            // Roadmap #39: reject here, not on the device - a device that received a malformed
            // window would just have scheduleRelayFunction() silently treat it as "never on"
            // (positionInCycle can never be negative), which is a confusing way to discover a typo.
            if (ScheduleWindowError(deviceUpdate.Controller) is string scheduleError)
            {
                return BadRequest(scheduleError);
            }

            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(deviceUpdate.Device.IDDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            await Repo.DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);
            return true;
        }

        /// <summary>Roadmap #39: v1 deliberately does not support a schedule window crossing local
        /// midnight (see api.Models.DeviceConfigController's comment) - Start+Duration must fit in
        /// one calendar day, and DaysOfWeek must fit the 7-bit mask AgrumyDevice's
        /// ControllerController::scheduleRelayFunction expects (bit 0 = Sunday .. bit 6 = Saturday).
        /// Returns the first validation failure found, or null if every enabled schedule is sound.</summary>
        private static string? ScheduleWindowError(DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return null;
            }

            (bool? Enabled, int? Days, int? Start, int? Duration, string Label)[] schedules =
            [
                (cfg.VentilationScheduleEnabled, cfg.VentilationScheduleDaysOfWeek, cfg.VentilationScheduleStart, cfg.VentilationScheduleDuration, "Ventilation"),
                (cfg.LightScheduleEnabled, cfg.LightScheduleDaysOfWeek, cfg.LightScheduleStart, cfg.LightScheduleDuration, "Light"),
                (cfg.HeatingScheduleEnabled, cfg.HeatingScheduleDaysOfWeek, cfg.HeatingScheduleStart, cfg.HeatingScheduleDuration, "Heating"),
                (cfg.WaterPumpScheduleEnabled, cfg.WaterPumpScheduleDaysOfWeek, cfg.WaterPumpScheduleStart, cfg.WaterPumpScheduleDuration, "Water pump"),
            ];

            foreach (var (enabled, days, start, duration, label) in schedules)
            {
                if (enabled != true)
                {
                    continue;
                }
                if (days is not int d || d < 0 || d > 0b1111111)
                {
                    return $"{label} schedule: days of week must be a value from 0 to 127.";
                }
                if (start is not int s || s < 0 || s > 86399)
                {
                    return $"{label} schedule: start must be between 0 and 86399 seconds since local midnight.";
                }
                if (duration is not int len || len < 1 || start + len > 86400)
                {
                    return $"{label} schedule: duration must be at least 1 second and not cross local midnight (start + duration <= 86400).";
                }
            }
            return null;
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

            // Roadmap #111: same ?? 0 fallback as #96/#102/#108 - without it a null-TenantID device
            // (rows written as TenantID=0 by DeviceAddAsync's own convention) never equals a
            // tenant-0 caller's CallerTenantId, so its own admin gets a 403 on their own device.
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if ((device.TenantID ?? 0) != CallerTenantId && !crossTenantAllowed)
            {
                return (device, StatusCode(403, $"{ownerLabel} belongs to a different tenant"));
            }
            return (device, null);
        }

        #endregion


        #region Device communication

        /// <summary>Roadmap #7: the poll body carries live diagnostics and the poll itself is the
        /// heartbeat - recorded before the version check so an up-to-date device still bumps
        /// LastSeenAt, which is what offline detection (#6/#8) stands on.</summary>
        [HttpPost("Config")]
        [EnableRateLimiting("device-auth")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<DeviceConfig>> GetConfig([FromBody] DeviceConfigPoll value)
        {
            string apiId = HttpContext.DeviceApiId()!;

            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return NotFound();
            }

            await Repo.DeviceDiagnosticUpsertAsync(device.IDDevice!.Value, device.TenantID ?? 0, value);

            // Roadmap #106: compare against the device row just read above, not a session-cache
            // copy - the cache entry can be stale/absent (5-min sliding TTL, #109) or written by a
            // different instance (#72), and this DB read already happens unconditionally for the
            // diagnostics upsert, so a second, independently-staled ConfigVersion added risk with
            // no savings.
            if (value.ConfigVersion == device.ConfigVersion)
            {
                return Ok(); // device is up to date - do nothing
            }

            return Ok(await BuildDeviceConfigAsync(device));
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
                    ApiId = Guid.NewGuid().ToString(), // identifier, not a secret - Guid is fine
                    ApiKey = AuthenticationProvider.GetSecureToken(), // roadmap #73: credential, needs a CSPRNG source
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

            // Roadmap #70 follow-up: the PIN is deliberately NOT consumed here - single-use made
            // registering many sensors in one session require regenerating a PIN between every
            // device, which the user judged "suludo" (absurd). It stays valid for repeated
            // registrations until its own 24h expiry; the 32^6 keyspace is what makes leaked/
            // shoulder-surfed reuse economically unattractive to brute-force, not single-use.
            return Ok(await BuildDeviceConfigAsync(device));
        }

        private async Task<DeviceConfig> BuildDeviceConfigAsync(Device device)
        {
            // Roadmap #39: computed fresh on every Config/Register response (cheap - one TimeZoneInfo
            // lookup) rather than cached, so a DST transition or an admin changing
            // ServerConfig.ScheduleTimeZone reaches every device on its very next poll. Sent
            // regardless of DeviceControllerEnabled - harmless for a sensor-only device, and one
            // fewer conditional for the firmware to reason about.
            int utcOffsetSeconds = TimeZoneHelper.GetUtcOffsetSeconds(DateTime.UtcNow, (await Repo.ServerConfigGetAsync(1)).ScheduleTimeZone);

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
                UtcOffsetSeconds = utcOffsetSeconds,
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

            // Roadmap #73: apiAuth is a bearer-style session credential (DeviceAuth.SessionPolicy),
            // same CSPRNG requirement as ApiKey above.
            var deviceAuthentication = new DeviceAuthentication { apiAuth = AuthenticationProvider.GetSecureToken() };
            await Cache.SetItemAsync(apiId, new DeviceCache { apiAuth = deviceAuthentication.apiAuth });

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
