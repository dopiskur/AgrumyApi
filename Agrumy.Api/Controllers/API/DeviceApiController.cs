using api.Commands;
using api.Dal.Interface;
using api.Devices;
using api.Firmware;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    [Route("/api/Device")]
    public class DeviceApiController(IRepository repo, ICache cache, CommandQueueService commandQueue, FirmwareCatalogService firmwareCatalog, DeviceConfigBuilder configBuilder) : ApiControllerBase(repo, cache)
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
            // A Global reader/Device/admin sees any tenant's device - DeviceGetAsync's tenant
            // filter would hide it, so use the unfiltered by-id lookup for them.
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

        /// <summary>Read-only status of every device at once, open to any authenticated caller;
        /// tenant scoping mirrors DevicesGet, with global readers seeing all tenants.</summary>
        [Authorize]
        [HttpGet("Fleet")]
        public async Task<ActionResult<IList<DeviceFleetStatus>>> DeviceFleetGet() =>
            Ok(await Repo.DeviceFleetGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        /// <summary>Diagnostic event log - open to any authenticated caller (a Tenant reader may
        /// look at their tenant's log); tenant ownership enforced the same way as every other
        /// Device sub-resource GET.</summary>
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

        /// <summary>Dismisses one non-critical problem alert (see api.Dal.EfRepository.ComputeStatus)
        /// so it stops keeping its device's Unit/Zone Orange - the event row itself stays for history,
        /// only its EventDeviceRow.AcknowledgedAt is set.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Event/{idEventDevice}/Acknowledge")]
        public async Task<ActionResult<bool>> DeviceEventAcknowledge(int idEventDevice)
        {
            bool updated = await Repo.EventDeviceAcknowledgeAsync(idEventDevice, CallerManagesDevicesGlobally ? null : CallerTenantId);
            if (!updated)
            {
                return NotFound();
            }
            return true;
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

        /// <summary>Only relay-pin mapping is left on the per-device Controller row - thresholds,
        /// schedule and safety limits live on the device's assigned zone instead
        /// (DeviceUnitApiController's Zone/Rule endpoints).</summary>
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
            return true;
        }

        /// <summary>Looks a device up and checks the caller may touch it. Returns (null, 404) when
        /// no device matches, (device, 403) on a tenant mismatch, or (device, null) when access is
        /// allowed. A foreign tenant's device passes for Global roles - CallerManagesDevicesGlobally
        /// on a write, the wider CallerReadsDevicesGlobally on a read.</summary>
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

        /// <summary>The poll itself is the heartbeat - diagnostics are recorded before the version
        /// check so an up-to-date device still bumps LastSeenAt, which offline detection stands on.</summary>
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

            // The heartbeat is also how the server learns an OTA actually took - the first poll
            // reporting the requested version fulfils the request (flags cleared, event logged).
            if (await firmwareCatalog.NoteHeartbeatAsync(device, value.FirmwareVersion, value.Board))
            {
                device.FirmwareUpdate = false;
                device.FirmwareTargetVersion = null;
                await Repo.EventDevicePushAsync(device.IDDevice.Value, device.TenantID, DeviceEventType.FirmwareUpdated, "version=" + value.FirmwareVersion);
            }

            // Compared against the device row just read above, not a session-cache copy - the cache
            // entry can be stale/absent or written by a different instance, and this DB read
            // already happens unconditionally for the diagnostics upsert above.
            //
            // A pending command must ride along on this SAME poll (no separate endpoint, no extra
            // TLS handshake), so config being unchanged alone is no longer enough to skip the response.
            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice.Value);
            if (value.ConfigVersion == device.ConfigVersion && pendingCommand == null)
            {
                return Ok(); // device is up to date and nothing is queued for it - do nothing
            }

            return Ok(await configBuilder.BuildAsync(device, pendingCommand, value.Board));
        }

        /// <summary>Arms an OTA for one device - Version null = latest catalog build for its board
        /// (one-click), a specific version = install exactly that (rollback/downgrade). The
        /// firmware's own "offered version != running" gate (ServiceController::apiConfig) makes a
        /// redundant request harmless; GetConfig clears it once the heartbeat confirms.</summary>
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

        /// <summary>No identity field in the body by design - deviceID/tenantID come exclusively
        /// from the authenticated apiId, same rule as SensorDataApiController.Post.</summary>
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

            // The device's post-execution confirmation rides on this same event-push endpoint -
            // CommandId links it back to the specific command row.
            if (eventType == DeviceEventType.CommandExecuted && value.CommandId is int commandId)
            {
                await commandQueue.MarkExecutedAsync(commandId, device.IDDevice!.Value);
            }

            return Ok();
        }

        /// <summary>The device confirms receipt of the PendingCommand it just got in its last
        /// Config poll response, BEFORE executing it - a Reboot has nothing to report from
        /// afterward on that connection, so ack-after-execute is not an option.</summary>
        [HttpPost("Command/Ack")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> AckCommand([FromBody] CommandAckRequest value)
        {
            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await commandQueue.AcknowledgeCommandAsync(value.CommandId, device.IDDevice!.Value);
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

            // Expiry and match failures share one generic 401 - a distinct "pin expired" reply
            // would confirm the email exists to an unauthenticated caller.
            User? user = await Repo.UserGetAsync(null, value.Email, null);
            if (user is null || !AuthenticationProvider.VerifyPin(user.DevicePin, user.DevicePinExpires, value.DevicePin))
            {
                return StatusCode(401, "Wrong user or pin");
            }

            Device? device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
            if (device is null)
            {
                device = await Repo.DeviceAddAsync(new Device
                {
                    ConfigVersion = 1,
                    TenantID = user.TenantID ?? 0,
                    DeviceName = "Agrumy_" + value.MacAddress.ToUpper(),
                    MacAddress = value.MacAddress,
                    ApiId = Guid.NewGuid().ToString(), // identifier, not a secret - Guid is fine
                    ApiKey = AuthenticationProvider.GetSecureToken(), // credential - needs a CSPRNG source, not Guid
                    ServicePoint = value.ServicePoint,
                    DeviceSensorEnabled = false,
                    DeviceControllerEnabled = false,
                    // Only Agrumy.Relay's own registration ever sets these - normal
                    // AgrumyFirmware devices leave both null/false, matching the DTO's defaults.
                    IsRelay = value.IsRelay,
                    RelayProfile = value.RelayProfile,
                });
            }

            // The PIN is deliberately NOT consumed here - it stays valid for repeated registrations
            // (multiple sensors in one session) until its own 24h expiry.
            //
            // A genuinely new device never has one, but Register also handles the "device row
            // already exists" re-registration path (factory reset, etc.), where a command could
            // legitimately still be queued.
            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice!.Value);
            // Register carries no Board - null falls back to the legacy per-type lookup.
            return Ok(await configBuilder.BuildAsync(device, pendingCommand, board: null));
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

            // apiAuth is a bearer-style session credential (DeviceAuth.SessionPolicy), same CSPRNG requirement as ApiKey above.
            var deviceAuthentication = new DeviceAuthentication { apiAuth = AuthenticationProvider.GetSecureToken() };
            await Cache.SetItemAsync(apiId, new DeviceCache { apiAuth = deviceAuthentication.apiAuth }, SessionTtlFor(device.SleepSeconds));

            return Ok(deviceAuthentication);
        }

        /// <summary>2x sleepSeconds absorbs a late wake without needing a session past the device's
        /// own next scheduled contact - avoids two TLS handshakes (Authenticate+Config) per wake on
        /// long-sleep solar/battery nodes. The 30-min floor keeps short-poll devices on the same
        /// cadence as before. Safe to extend since the cache entry holds only apiAuth, nothing that goes stale.</summary>
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
