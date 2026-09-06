using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Agrumy.Gateway's own endpoints - a gateway is a device row like any other (see api.Models.Device.IsGateway) that authenticates the same way (DeviceAuth.ApiKeyPolicy, its own apiId/apiKey), just forwarding OTHER devices' traffic through Batch instead of reporting its own sensors.
    [Route("/api/Gateway")]
    public class GatewayApiController(
        IDeviceRepository deviceRepo, IServerConfigRepository serverConfigRepo, ISensorDataRepository sensorDataRepo, IGatewayRepository gatewayRepo,
        IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache, CommandQueueService commandQueue,
        FirmwareCatalogService firmwareCatalog, DeviceConfigBuilder configBuilder)
        : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        /// The caller's own device row, already confirmed to be a gateway - null (with the ActionResult already set) covers every failure mode, so every action below is one guard clause instead of repeating the same checks.
        private async Task<(Device? gateway, ActionResult? error)> GetCallerGatewayAsync()
        {
            string apiId = HttpContext.DeviceApiId()!;
            Device? gateway = await deviceRepo.DeviceGetByApiIdAsync(apiId);
            if (gateway is null)
            {
                return (null, NotFound());
            }
            if (gateway.IsGateway != true && gateway.LoRaGatewayEnabled != true)
            {
                return (null, StatusCode(403, "This device is not registered as a Gateway."));
            }
            ServerConfig serverConfig = await serverConfigRepo.ServerConfigGetAsync(1);
            if (!serverConfig.GatewayEnabled)
            {
                return (null, StatusCode(403, "Gateway support is disabled server-wide (Server Settings -> Gateway)."));
            }
            return (gateway, null);
        }

        // 500 gives generous headroom for either a small LoRa aggregation batch or a larger WiFi-repeater one, while still bounding a malformed/hostile batch's server-side work.
        private const int MaxBatchEntries = 500;

        /// Iterates a gateway's batched entries, running each through the SAME logic its wrapped single-device endpoint (Config/SensorData/Event/Command.Ack) already uses (see the per-type handlers below) - one entry failing never fails the rest of the batch.
        [HttpPost("Batch")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<GatewayBatchResponse>> Batch([FromBody] GatewayBatchRequest request)
        {
            var (gateway, error) = await GetCallerGatewayAsync();
            if (error != null)
            {
                return error;
            }

            if (request.Entries.Count > MaxBatchEntries)
            {
                return BadRequest($"Batch too large: {request.Entries.Count} entries, max {MaxBatchEntries}.");
            }

            ServerConfig serverConfig = await serverConfigRepo.ServerConfigGetAsync(1);
            var results = new List<GatewayBatchEntryResult>(request.Entries.Count);
            foreach (GatewayBatchEntry entry in request.Entries)
            {
                results.Add(await RunEntryAsync(entry, gateway!.TenantID));
            }

            return Ok(new GatewayBatchResponse
            {
                Results = results,
                GatewayMode = serverConfig.GatewayMode,
                GatewayWaitWindowSeconds = serverConfig.GatewayWaitWindowSeconds,
            });
        }

        private async Task<GatewayBatchEntryResult> RunEntryAsync(GatewayBatchEntry entry, int gatewayTenantId)
        {
            Device? device = await deviceRepo.DeviceGetByApiIdAsync(entry.DeviceApiId);
            if (device is null || !DeviceAuth.ConstantTimeEquals(entry.DeviceApiKey, device.ApiKey))
            {
                return new GatewayBatchEntryResult { Success = false, StatusCode = 401, Error = "Unknown device or apiKey mismatch." };
            }
            if (device.TenantID != gatewayTenantId)
            {
                return new GatewayBatchEntryResult { Success = false, StatusCode = 403, Error = "Device belongs to a different tenant than this gateway." };
            }

            try
            {
                return entry.Type switch
                {
                    GatewayEntryType.Config => await RunConfigAsync(device, entry.Payload),
                    GatewayEntryType.SensorData => await RunSensorDataAsync(device, entry.Payload),
                    GatewayEntryType.Event => await RunEventAsync(device, entry.Payload),
                    GatewayEntryType.CommandAck => await RunCommandAckAsync(device, entry.Payload),
                    _ => new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unknown entry type: {entry.Type}" },
                };
            }
            catch (JsonException ex)
            {
                // A malformed entry must not take the rest of the batch down with it - same reason SensorController.flushBufferedSensorData() drops a poison file instead of wedging.
                return new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = "Malformed payload: " + ex.Message };
            }
        }

        /// Same steps as DeviceApiController.GetConfig, in the same order - diagnostics upsert before the version check, so a batched device still bumps LastSeenAt even when nothing changed.
        private async Task<GatewayBatchEntryResult> RunConfigAsync(Device device, JsonElement payload)
        {
            DeviceConfigPoll poll = payload.Deserialize<DeviceConfigPoll>() ?? new DeviceConfigPoll();

            await deviceRepo.DeviceDiagnosticUpsertAsync(device.IDDevice!.Value, device.TenantID, poll);

            if (await firmwareCatalog.NoteHeartbeatAsync(device, poll.FirmwareVersion, poll.Board))
            {
                device.FirmwareUpdate = false;
                device.FirmwareTargetVersion = null;
                await deviceRepo.EventDevicePushAsync(device.IDDevice.Value, device.TenantID, DeviceEventType.FirmwareUpdated, "version=" + poll.FirmwareVersion);
            }

            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice.Value);
            if (!await configBuilder.NeedsRefreshAsync(device, poll.ConfigVersion, pendingCommand))
            {
                return new GatewayBatchEntryResult { Success = true, StatusCode = 200 }; // up to date, nothing queued, no heartbeat due - mirrors GetConfig's empty-200
            }

            DeviceConfig config = await configBuilder.BuildAsync(device, pendingCommand, poll.Board);
            await deviceRepo.DeviceMarkConfigSentAsync(device.IDDevice!.Value, DateTime.UtcNow);
            return new GatewayBatchEntryResult { Success = true, StatusCode = 200, Config = config };
        }

        /// Same steps as SensorDataController.Post.
        private async Task<GatewayBatchEntryResult> RunSensorDataAsync(Device device, JsonElement payload)
        {
            JsonArray jsonArray = JsonNode.Parse(payload.GetRawText()) as JsonArray
                ?? throw new JsonException("SensorData payload must be a JSON array.");

            await sensorDataRepo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceUnitID, device.DeviceUnitZoneID);

            return new GatewayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// Same steps as DeviceApiController.PushEvent.
        private async Task<GatewayBatchEntryResult> RunEventAsync(Device device, JsonElement payload)
        {
            DeviceEventPush push = payload.Deserialize<DeviceEventPush>() ?? new DeviceEventPush();
            if (!Enum.TryParse<DeviceEventType>(push.EventType, ignoreCase: true, out var eventType))
            {
                return new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unknown eventType: {push.EventType}" };
            }

            await deviceRepo.EventDevicePushAsync(device.IDDevice!.Value, device.TenantID, eventType, push.Message);

            if (eventType == DeviceEventType.CommandExecuted && push.CommandId is int commandId)
            {
                await commandQueue.MarkExecutedAsync(commandId, device.IDDevice!.Value);
            }

            return new GatewayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// Same steps as DeviceApiController.AckCommand.
        private async Task<GatewayBatchEntryResult> RunCommandAckAsync(Device device, JsonElement payload)
        {
            CommandAckRequest ack = payload.Deserialize<CommandAckRequest>() ?? new CommandAckRequest();
            await commandQueue.AcknowledgeCommandAsync(ack.CommandId, device.IDDevice!.Value);
            return new GatewayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// What the OWNING gateway itself fetches to build its DevEUI-&gt;apiId/apiKey forwarding cache (GatewayProfile.LoRaGateway) - includes ApiKey, unlike the admin list below, since Gateway must reconstruct each mapped device's own request.
        [HttpGet("DeviceMapping")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<IList<GatewayDeviceMapping>>> DeviceMappingGetMine()
        {
            var (gateway, error) = await GetCallerGatewayAsync();
            if (error != null)
            {
                return error;
            }
            return Ok(await gatewayRepo.GatewayDeviceMappingsWithSecretsGetAsync(gateway!.IDDevice!.Value));
        }

        /// Roadmap #383 - the WiFi-relay counterpart to LoRaGatewayBridgeController's serial link: one already RF-decoded frame, resolved against the CALLER's own GatewayDeviceMapping (address stored in DevEUI) and dispatched through the same Config/SensorData/Event/CommandAck handlers Batch uses. A small, fixed catalog (a handful of mapped nodes per gateway at most) - no reason for a per-request cache like Gateway's own client-side one.
        [HttpPost("RelayUplink")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<GatewayBatchEntryResult>> RelayUplink([FromBody] GatewayRelayUplinkRequest request)
        {
            var (gateway, error) = await GetCallerGatewayAsync();
            if (error != null)
            {
                return error;
            }

            IList<GatewayDeviceMapping> mappings = await gatewayRepo.GatewayDeviceMappingsGetAsync(gateway!.IDDevice!.Value);
            string address = request.SourceAddress.ToString();
            GatewayDeviceMapping? mapping = mappings.FirstOrDefault(m => m.DevEUI == address);
            if (mapping?.IDDevice is not int idDevice)
            {
                return Ok(new GatewayBatchEntryResult { Success = false, StatusCode = 404, Error = $"Node address {request.SourceAddress} is not mapped for this gateway." });
            }
            Device? device = await deviceRepo.DeviceGetByIdAsync(idDevice);
            if (device is null)
            {
                return Ok(new GatewayBatchEntryResult { Success = false, StatusCode = 404, Error = "Mapped device no longer exists." });
            }

            JsonNode? envelope;
            try
            {
                envelope = JsonNode.Parse(request.Payload);
            }
            catch (JsonException ex)
            {
                return Ok(new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = "Malformed payload: " + ex.Message });
            }
            string? entryTypeTag = envelope?["t"]?.GetValue<string>();
            GatewayEntryType? entryType = entryTypeTag switch
            {
                "config" => GatewayEntryType.Config,
                "sensor" => GatewayEntryType.SensorData,
                "event" => GatewayEntryType.Event,
                "ack" => GatewayEntryType.CommandAck,
                _ => null,
            };
            if (entryType is null || envelope is null)
            {
                return Ok(new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unrecognized envelope type: {entryTypeTag}" });
            }

            // Same reasoning as LoRaPrivateProtocolUplinkService.ProcessUplinkAsync: SensorData needs an array payload, the firmware nests readings under "d".
            JsonElement payload = entryType == GatewayEntryType.SensorData && envelope["d"] is JsonNode sensorArray
                ? JsonDocument.Parse(sensorArray.ToJsonString()).RootElement
                : JsonDocument.Parse(envelope.ToJsonString()).RootElement;

            try
            {
                GatewayBatchEntryResult result = entryType switch
                {
                    GatewayEntryType.Config => await RunConfigAsync(device, payload),
                    GatewayEntryType.SensorData => await RunSensorDataAsync(device, payload),
                    GatewayEntryType.Event => await RunEventAsync(device, payload),
                    GatewayEntryType.CommandAck => await RunCommandAckAsync(device, payload),
                    _ => new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unrecognized envelope type: {entryTypeTag}" },
                };
                return Ok(result);
            }
            catch (JsonException ex)
            {
                return Ok(new GatewayBatchEntryResult { Success = false, StatusCode = 400, Error = "Malformed payload: " + ex.Message });
            }
        }

        // ---- admin (Gateway Devices page) ---------------------------------------------------

        [HttpGet("All")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<IList<DeviceDto>>> GatewaysGetAll() =>
            Ok((await gatewayRepo.GatewayDevicesGetAllAsync()).Select(d => d.ToDto()).ToList());

        /// Looks the gateway device up and checks the caller may touch it - same shared 404/403 logic every other Device-domain controller uses (ApiControllerBase.EnsureOwnedDeviceEntityAsync).
        private Task<(Device? Gateway, ActionResult? Error)> EnsureOwnedGatewayAsync(int idGatewayDevice, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => deviceRepo.DeviceGetByIdAsync(idGatewayDevice), d => d.TenantID, "Gateway", forWrite);

        [HttpGet("DeviceMapping/All")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<IList<GatewayDeviceMapping>>> DeviceMappingGetAll(int idGatewayDevice)
        {
            var (_, error) = await EnsureOwnedGatewayAsync(idGatewayDevice, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await gatewayRepo.GatewayDeviceMappingsGetAsync(idGatewayDevice));
        }

        [HttpPost("DeviceMapping")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<bool>> DeviceMappingAdd([FromBody] GatewayDeviceMapping value)
        {
            if (string.IsNullOrWhiteSpace(value.DevEUI) || value.IDGatewayDevice is not int idGateway || value.IDDevice is not int idDevice)
            {
                return BadRequest("IDGatewayDevice, DevEUI and IDDevice are required.");
            }
            var (gateway, error) = await EnsureOwnedGatewayAsync(idGateway, forWrite: true);
            if (error != null)
            {
                return error;
            }
            bool added = await gatewayRepo.GatewayDeviceMappingAddAsync(idGateway, value.DevEUI.Trim().ToUpperInvariant(), idDevice, gateway!.TenantID);
            return added ? Ok(true) : Conflict("That DevEUI is already mapped for this gateway, or IDDevice does not exist or belongs to a different tenant than the gateway.");
        }

        [HttpDelete("DeviceMapping")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<bool>> DeviceMappingDelete(int idGatewayDeviceMapping, int idGatewayDevice)
        {
            var (_, error) = await EnsureOwnedGatewayAsync(idGatewayDevice, forWrite: true);
            if (error != null)
            {
                return error;
            }
            return Ok(await gatewayRepo.GatewayDeviceMappingDeleteAsync(idGatewayDeviceMapping, idGatewayDevice));
        }
    }
}
