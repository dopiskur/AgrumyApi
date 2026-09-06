using api.Commands;
using api.Dal.Interface;
using api.Devices;
using api.Firmware;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace api.Controllers.API
{
    [Route("/api/Device")]
    public class DeviceApiController(IRepository repo, ICache cache, CommandQueueService commandQueue, FirmwareCatalogService firmwareCatalog, DeviceConfigBuilder configBuilder, IOptions<AgrumySettings> settingsOptions) : ApiControllerBase(repo, cache)
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        #region websvc api

        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<IEnumerable<DeviceDto>>> DevicesGet() =>
            Ok((CallerReadsDevicesGlobally ? await Repo.DevicesGetAllAsync() : await Repo.DevicesGetAsync(CallerTenantId))
                .Select(d => d.ToDto()));

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<DeviceDto>> DeviceGet(int? idDevice)
        {
            // A Global reader/Device/admin sees any tenant's device - DeviceGetAsync's tenant filter would hide it, so use the unfiltered by-id lookup for them.
            Device? device = CallerReadsDevicesGlobally
                ? await Repo.DeviceGetByIdAsync(idDevice)
                : await Repo.DeviceGetAsync(CallerTenantId, idDevice, null, null);
            return device is null ? NotFound() : Ok(device.ToDto());
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUpdate([FromBody] DeviceDto device)
        {
            var (existing, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(device.IDDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }

            Device internalDevice = device.ToDevice();
            internalDevice.TenantID = existing!.TenantID; // payload cannot move a device to another tenant

            await Repo.DeviceUpdateAsync(internalDevice);
            await WriteAuditAsync("Device.Updated", existing.TenantID, "Device", existing.IDDevice.ToString()!, existing.DeviceName);
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

            // The device's OWN tenant, not the caller's - a Global admin/Device deleting a foreign tenant's device would otherwise silently match zero rows.
            await Repo.DeviceDeleteAsync(idDevice, device!.TenantID);
            await WriteAuditAsync("Device.Deleted", device.TenantID, "Device", idDevice.ToString()!, device.DeviceName);
            return true;
        }

        [Authorize]
        [HttpGet("Sensor")]
        public async Task<ActionResult<DeviceConfigSensor>> DeviceConfigSensorGet(int? deviceConfigSensorID)
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view device configuration.");
            }
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
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view device configuration.");
            }
            var (_, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByDeviceConfigControllerIdAsync(deviceConfigControllerID), "Controller config", forWrite: false);
            if (error != null)
            {
                return error;
            }

            return Ok(await Repo.DeviceConfigControllerGetAsync(deviceConfigControllerID));
        }

        /// Read-only status of every device at once, open to any authenticated caller; tenant scoping mirrors DevicesGet, with global readers seeing all tenants.
        [Authorize]
        [HttpGet("Fleet")]
        public async Task<ActionResult<IList<DeviceFleetStatus>>> DeviceFleetGet() =>
            Ok(await Repo.DeviceFleetGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        /// Same status DeviceFleetGet carries for one device, without scanning the whole fleet - for a single-device detail page.
        [Authorize]
        [HttpGet("FleetStatus")]
        public async Task<ActionResult<DeviceFleetStatus>> DeviceFleetStatusGet(int idDevice)
        {
            DeviceFleetStatus? status = await Repo.DeviceFleetStatusGetAsync(idDevice, CallerReadsDevicesGlobally ? null : CallerTenantId);
            return status is null ? NotFound() : Ok(status);
        }

        /// Diagnostic event log, open to any authenticated caller (a Tenant reader sees their own tenant's log); tenant ownership enforced the same way as every other Device sub-resource GET.
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

            // The device's own tenant (== the caller's for a tenant-scoped caller; the ensure call above already authorized a cross-tenant global reader).
            return Ok(await Repo.EventDeviceGetAsync(device!.IDDevice, device.TenantID));
        }

        /// Dismisses one non-critical problem alert (see api.Dal.EfRepository.ComputeStatus) so it stops keeping its device's Unit/Zone Orange - only EventDeviceRow.AcknowledgedAt is set, the event row itself stays for history.
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

        /// Only relay-pin mapping is left on the per-device Controller row - thresholds, schedule and safety limits live on the device's assigned zone instead (DeviceUnitApiController's Zone/Rule endpoints).
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

            string? problem = await Repo.DeviceConfigControllerUpdateAsync(deviceUpdate.Device.IDDevice, deviceUpdate.Controller);
            if (problem != null)
            {
                return BadRequest(problem);
            }
            return true;
        }

        /// Looks a device up and checks the caller may touch it - see ApiControllerBase.EnsureOwnedDeviceEntityAsync for the shared 404/403 logic.
        private Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(
            Func<Task<Device?>> lookup, string ownerLabel, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(lookup, d => d.TenantID, ownerLabel, forWrite);

        #endregion


        #region Device communication

        /// The poll itself is the heartbeat - diagnostics are recorded before the version check so an up-to-date device still bumps LastSeenAt, which offline detection stands on.
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

            // The heartbeat is also how the server learns an OTA actually took - the first poll reporting the requested version fulfils the request (flags cleared, event logged).
            if (await firmwareCatalog.NoteHeartbeatAsync(device, value.FirmwareVersion, value.Board))
            {
                device.FirmwareUpdate = false;
                device.FirmwareTargetVersion = null;
                await Repo.EventDevicePushAsync(device.IDDevice.Value, device.TenantID, DeviceEventType.FirmwareUpdated, "version=" + value.FirmwareVersion);
            }

            // Compared against the device row read above (not a stale/absent session-cache copy) - config-unchanged alone is no longer enough to skip the response, since a pending command must ride along on this same poll.
            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice.Value);
            if (!await configBuilder.NeedsRefreshAsync(device, value.ConfigVersion, pendingCommand))
            {
                return Ok(); // device is up to date, nothing is queued for it, and no heartbeat resend is due - do nothing
            }

            DeviceConfig config = await configBuilder.BuildAsync(device, pendingCommand, value.Board);
            await Repo.DeviceMarkConfigSentAsync(device.IDDevice.Value, DateTime.UtcNow);
            return Ok(config);
        }

        /// Arms an OTA for one device - Version null means latest catalog build for its board (one-click), a specific version installs exactly that (rollback/downgrade); the firmware's own offered-vs-running gate (ServiceController::apiConfig) makes a redundant request harmless, GetConfig clears it once the heartbeat confirms.
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

        /// No identity field in the body by design - deviceID/tenantID come exclusively from the authenticated apiId, same rule as SensorDataApiController.Post.
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

            // The device's post-execution confirmation rides on this same event-push endpoint - CommandId links it back to the specific command row.
            if (eventType == DeviceEventType.CommandExecuted && value.CommandId is int commandId)
            {
                await commandQueue.MarkExecutedAsync(commandId, device.IDDevice!.Value);
            }

            return Ok();
        }

        /// The device confirms receipt of the PendingCommand from its last Config poll response BEFORE executing it - a Reboot has nothing to report afterward on that connection, so ack-after-execute isn't an option.
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

            // Expiry and match failures share one generic 401 - a distinct "pin expired" reply would confirm the email exists to an unauthenticated caller.
            User? user = await Repo.UserGetAsync(null, value.Email, null);
            if (user is null || !AuthenticationProvider.VerifyPin(user.DevicePin, user.DevicePinExpires, value.DevicePin))
            {
                return StatusCode(401, "Wrong user or pin");
            }

            Device? device = await Repo.DeviceGetAsync(user.TenantID, null, null, value.MacAddress);
            if (device is null)
            {
                // A client merely holding a valid user email+PIN (the same bar every ordinary device meets) must not be able to claim gateway status on its own say-so - only honored when it also proves it's the real Agrumy.Gateway via this shared secret.
                bool provenGateway = value.IsGateway
                    && !string.IsNullOrEmpty(settings.GatewayRegistrationSecret)
                    && DeviceAuth.ConstantTimeEquals(value.GatewayRegistrationSecret, settings.GatewayRegistrationSecret);

                // This mac may be the target of an earlier Discovery/Register call whose queued ProvisionDevice command carries the DeviceName/Zone the admin picked then.
                DiscoveryProvisionPayload? provision = await commandQueue.ConsumePendingProvisionAsync(value.MacAddress);

                device = await Repo.DeviceAddAsync(new Device
                {
                    ConfigVersion = 1,
                    TenantID = user.TenantID ?? 0,
                    // Discovery-provisioned name (admin, pre-registration) beats the captive-portal one (device owner, at setup) beats the generic default.
                    DeviceName = !string.IsNullOrWhiteSpace(provision?.DeviceName) ? provision.DeviceName
                        : !string.IsNullOrWhiteSpace(value.DisplayName) ? value.DisplayName
                        : "Agrumy_" + value.MacAddress.ToUpper(),
                    MacAddress = value.MacAddress,
                    ApiId = Guid.NewGuid().ToString(), // identifier, not a secret - Guid is fine
                    ApiKey = AuthenticationProvider.GetSecureToken(), // credential - needs a CSPRNG source, not Guid
                    ServicePoint = value.ServicePoint,
                    DeviceSensorEnabled = false,
                    DeviceControllerEnabled = false,
                    IsGateway = provenGateway,
                    GatewayProfile = provenGateway ? value.GatewayProfile : null,
                });

                if (provision?.ZoneID is int zoneId)
                {
                    await Repo.DeviceAssignToZoneAsync(device.IDDevice!.Value, zoneId);
                }
            }

            // The PIN is deliberately NOT consumed here (stays valid for repeated registrations until its own 24h expiry), and Register also handles re-registration, where a pending command could legitimately still be queued.
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

        /// 2x sleepSeconds absorbs a late wake without a second TLS handshake (Authenticate+Config) on long-sleep nodes, with a 30-min floor for short-poll devices - safe to extend since the cache entry holds only apiAuth, nothing that goes stale.
        private static TimeSpan SessionTtlFor(int? sleepSeconds) =>
            TimeSpan.FromSeconds(Math.Max((sleepSeconds ?? 0) * 2, 1800));

        #endregion

        #region Device Roles

        [HttpGet("Role")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<DeviceRole>>> DeviceRoleGet() =>
            Ok(await Repo.DeviceRoleGetAsync());

        /// Backs the Web Device Edit form's "Manual Kit" dropdown.
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

        #region Simulation Mode

        /// Device-facing poll - identity comes from the authenticated apiId, same rule as PushEvent/AckCommand, never a route parameter. No content when Simulation Mode isn't enabled, so firmware has a cheap "nothing to override" signal without parsing a body full of nulls.
        [HttpGet("Simulation")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<DeviceSimulation>> DeviceSimulationPoll()
        {
            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            DeviceSimulation? sim = await Repo.DeviceSimulationGetAsync(device.IDDevice!.Value);
            return sim is { Enabled: true } ? Ok(sim) : NoContent();
        }

        /// Admin read for the Web Simulation page - an empty, disabled DeviceSimulation (not 404) when the device has never had one set, so the form has something to bind to.
        [HttpGet("Simulation/{idDevice}")]
        [Authorize(Roles = RoleNames.SimulationManagers)]
        public async Task<ActionResult<DeviceSimulation>> DeviceSimulationGet(int idDevice)
        {
            var (_, error) = await EnsureOwnedDeviceAsync(() => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await Repo.DeviceSimulationGetAsync(idDevice) ?? new DeviceSimulation());
        }

        [HttpPut("Simulation/{idDevice}")]
        [Authorize(Roles = RoleNames.SimulationManagers)]
        public async Task<ActionResult> DeviceSimulationSet(int idDevice, [FromBody] DeviceSimulation value)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(() => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }
            await Repo.DeviceSimulationSetAsync(idDevice, value);
            await WriteAuditAsync("Device.SimulationSet", device!.TenantID, "Device", idDevice.ToString(), device.DeviceName);
            return Ok();
        }

        #endregion
    }
}
