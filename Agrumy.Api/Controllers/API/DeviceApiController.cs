using api.Commands;
using api.Dal.Interface;
using api.Firmware;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    [Route("/api/Device")]
    public class DeviceApiController(IRepository repo, ICache cache, CommandQueueService commandQueue, FirmwareCatalogService firmwareCatalog) : ApiControllerBase(repo, cache)
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

        /// <summary>Roadmap #39/#115: v1 deliberately does not support a schedule window crossing
        /// local midnight (see api.Models.DeviceConfigController's comment) - Start+Duration must
        /// fit in one calendar day, and DaysOfWeek must fit the 7-bit mask AgrumyFirmware's
        /// ActuatorController::scheduleRelayFunction expects (bit 0 = Sunday .. bit 6 = Saturday).
        /// Every slot in every function's list is checked (no more per-function Enabled gate - a
        /// slot's presence in the list already means it is active); returns the first failure
        /// found, or null if every slot is sound. A device-side cap on how many slots actually get
        /// used (MAX_SCHEDULE_SLOTS_PER_FUNCTION, AgrumyFirmware's RelayLogic.h) is NOT enforced here
        /// deliberately - a caller sending more than the firmware can hold just gets extras it
        /// silently ignores, not a hard save-time rejection tied to one particular firmware build.</summary>
        private static string? ScheduleWindowError(DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return null;
            }

            (IEnumerable<DeviceScheduleSlot>? Slots, string Label)[] groups =
            [
                (cfg.VentilationSchedule, "Ventilation"),
                (cfg.LightSchedule, "Light"),
                (cfg.HeatingSchedule, "Heating"),
                (cfg.WaterPumpSchedule, "Water pump"),
            ];

            foreach (var (slots, label) in groups)
            {
                foreach (var slot in slots ?? [])
                {
                    if (slot.DaysOfWeek < 0 || slot.DaysOfWeek > 0b1111111)
                    {
                        return $"{label} schedule: days of week must be a value from 0 to 127.";
                    }
                    if (slot.Start < 0 || slot.Start > 86399)
                    {
                        return $"{label} schedule: start must be between 0 and 86399 seconds since local midnight.";
                    }
                    if (slot.Duration < 1 || slot.Start + slot.Duration > 86400)
                    {
                        return $"{label} schedule: duration must be at least 1 second and not cross local midnight (start + duration <= 86400).";
                    }
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

            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (device.TenantID != CallerTenantId && !crossTenantAllowed)
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

            await Repo.DeviceDiagnosticUpsertAsync(device.IDDevice!.Value, device.TenantID, value);

            // Roadmap #93: the heartbeat is also how the server learns an OTA actually took - the
            // first poll reporting the requested version fulfils the request (flags cleared, event
            // logged) so the UI's "pending" state and the poll's OTA offer both stop.
            if (await firmwareCatalog.NoteHeartbeatAsync(device, value.FirmwareVersion, value.Board))
            {
                device.FirmwareUpdate = false;
                device.FirmwareTargetVersion = null;
                await Repo.EventDevicePushAsync(device.IDDevice.Value, device.TenantID, DeviceEventType.FirmwareUpdated, "version=" + value.FirmwareVersion);
            }

            // Roadmap #106: compare against the device row just read above, not a session-cache
            // copy - the cache entry can be stale/absent (5-min sliding TTL, #109) or written by a
            // different instance (#72), and this DB read already happens unconditionally for the
            // diagnostics upsert, so a second, independently-staled ConfigVersion added risk with
            // no savings.
            //
            // Roadmap #34: the empty-body short-circuit now also checks for a pending command -
            // config being unchanged is no longer enough on its own to skip the response, since a
            // command needs to ride along on this SAME poll (no separate endpoint, no extra TLS
            // handshake). GetPendingCommandAsync is cheap (one indexed query, no pending command in
            // the overwhelming common case) so paying it on every poll is fine.
            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice.Value);
            if (value.ConfigVersion == device.ConfigVersion && pendingCommand == null)
            {
                return Ok(); // device is up to date and nothing is queued for it - do nothing
            }

            return Ok(await BuildDeviceConfigAsync(device, pendingCommand, value.Board));
        }

        /// <summary>Roadmap #93: arms an OTA for one device - Version null = latest catalog build
        /// for its board (one-click), a specific version = install exactly that (rollback/downgrade).
        /// The firmware's own "offered version != running" gate (ServiceController::apiConfig) means
        /// a redundant request is harmless; GetConfig clears it once the heartbeat confirms.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("FirmwareUpdate")]
        public async Task<ActionResult> FirmwareUpdateRequest([FromBody] DeviceFirmwareUpdateRequest request)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(request.IdDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }
            string? problem = await firmwareCatalog.RequestUpdateAsync(device!, request.Version);
            return problem == null ? Ok() : BadRequest(problem);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("FirmwareUpdate")]
        public async Task<ActionResult> FirmwareUpdateCancel(int idDevice)
        {
            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }
            await firmwareCatalog.CancelUpdateAsync(idDevice);
            return Ok();
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

            await Repo.EventDevicePushAsync(device.IDDevice!.Value, device.TenantID, eventType, value.Message);

            // Roadmap #34: the device's post-execution confirmation rides on the SAME existing
            // event-push endpoint (#28) rather than a new one - CommandId links it back to the
            // specific command row. No ownership check needed beyond the session auth already
            // required above (the same rule the rest of this endpoint already follows).
            if (eventType == DeviceEventType.CommandExecuted && value.CommandId is int commandId)
            {
                await commandQueue.MarkExecutedAsync(commandId);
            }

            return Ok();
        }

        /// <summary>Roadmap #34: the device confirms receipt of the PendingCommand it just got in
        /// this same session's last Config poll response, BEFORE executing it - a Reboot has
        /// nothing to report from afterward on this connection, so ack-after-execute is not an
        /// option (this is the "novi API poziv" mechanism the design left open, decided here: a
        /// small dedicated endpoint, not piggybacked on the next poll's request body). No ownership
        /// check beyond session auth - same rule as PushEvent above.</summary>
        [HttpPost("Command/Ack")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> AckCommand([FromBody] CommandAckRequest value)
        {
            await commandQueue.AcknowledgeCommandAsync(value.CommandId);
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
                    TenantID = user.TenantID ?? 0, // User.TenantID stays nullable (out of #112's scope)
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
            //
            // Roadmap #34: a genuinely NEW device never has one, but Register also handles the
            // "device row already exists" re-registration path (factory reset, etc.), where a
            // command could legitimately still be queued.
            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice!.Value);
            // Register carries no Board (roadmap #94) - null falls back to the legacy per-type lookup.
            return Ok(await BuildDeviceConfigAsync(device, pendingCommand, board: null));
        }

        private async Task<DeviceConfig> BuildDeviceConfigAsync(Device device, PendingCommand? pendingCommand, string? board)
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
                CommandVersion = device.CommandVersion,
                PendingCommand = pendingCommand,
            };

            // Roadmap #3 (OTA) / #94: the firmware does a version comparison of its own so an offer
            // being present on every Config sync is fine. Harmless on Register: a freshly-created
            // device has FirmwareUpdate == null, and ResolveOfferAsync returns null for that first.
            DeviceFirmware? firmware = await firmwareCatalog.ResolveOfferAsync(device, board);
            if (firmware != null)
            {
                deviceConfig.FirmwareVersion = firmware.Version;
                deviceConfig.FirmwareUrl = firmware.Url;
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
            await Cache.SetItemAsync(apiId, new DeviceCache { apiAuth = deviceAuthentication.apiAuth }, SessionTtlFor(device.SleepSeconds));

            return Ok(deviceAuthentication);
        }

        /// <summary>Roadmap #109: the old fixed 5-min sliding TTL meant a device sleeping longer
        /// than that (the #89 dropdown goes up to 24h, #26 deep sleep) lost its session every
        /// single cycle - two TLS handshakes (Authenticate+Config) on every wake instead of one,
        /// real battery cost on the solar/battery nodes that actually use long sleeps. 2x
        /// sleepSeconds absorbs a late wake without needing a session past the device's own next
        /// scheduled contact; the 30-min floor keeps short-poll devices on the same cadence as
        /// before. Safe now that #106 removed ConfigVersion from the cache entry - nothing here
        /// goes stale by living longer, only apiAuth remains.</summary>
        private static TimeSpan SessionTtlFor(int? sleepSeconds) =>
            TimeSpan.FromSeconds(Math.Max((sleepSeconds ?? 0) * 2, 1800));

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
