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
    /// <summary>Agrumy.Relay's own endpoints. A relay is a device row like any
    /// other (see api.Models.Device.IsRelay) - it authenticates the SAME way a device does
    /// (DeviceAuth.ApiKeyPolicy, its own permanent apiId/apiKey), it just forwards OTHER devices'
    /// traffic through Batch instead of reporting its own sensors.</summary>
    [Route("/api/Relay")]
    public class RelayApiController(
        IRepository repo, ICache cache, CommandQueueService commandQueue,
        FirmwareCatalogService firmwareCatalog, DeviceConfigBuilder configBuilder)
        : ApiControllerBase(repo, cache)
    {
        /// <summary>The caller's own device row, already confirmed to be a relay - null (with the
        /// ActionResult already set) covers every way that can fail, so every action below is one
        /// guard clause instead of repeating the same three checks.</summary>
        private async Task<(Device? relay, ActionResult? error)> GetCallerRelayAsync()
        {
            string apiId = HttpContext.DeviceApiId()!;
            Device? relay = await Repo.DeviceGetByApiIdAsync(apiId);
            if (relay is null)
            {
                return (null, NotFound());
            }
            if (relay.IsRelay != true)
            {
                return (null, StatusCode(403, "This device is not registered as a Relay."));
            }
            ServerConfig serverConfig = await Repo.ServerConfigGetAsync(1);
            if (!serverConfig.RelayEnabled)
            {
                return (null, StatusCode(403, "Relay support is disabled server-wide (Server Settings -> Relay)."));
            }
            return (relay, null);
        }

        // A LoRa uplink batch is naturally small (one gateway's worth of devices in one aggregation
        // window); a WiFi-repeater batch could in principle be large, but still bounded by however
        // many devices physically sit behind one relay - 500 gives generous headroom either way
        // without letting a malformed/hostile batch force unbounded server-side work.
        private const int MaxBatchEntries = 500;

        /// <summary>Iterates a relay's batched entries, running each through the SAME logic its
        /// wrapped single-device endpoint (Config/SensorData/Event/Command.Ack) already uses - see
        /// api.Devices.DeviceConfigBuilder and the per-type handlers below, none of it duplicated.
        /// One entry failing (wrong apiKey, unknown apiId) never fails the rest of the batch.</summary>
        [HttpPost("Batch")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<RelayBatchResponse>> Batch([FromBody] RelayBatchRequest request)
        {
            var (relay, error) = await GetCallerRelayAsync();
            if (error != null)
            {
                return error;
            }

            if (request.Entries.Count > MaxBatchEntries)
            {
                return BadRequest($"Batch too large: {request.Entries.Count} entries, max {MaxBatchEntries}.");
            }

            ServerConfig serverConfig = await Repo.ServerConfigGetAsync(1);
            var results = new List<RelayBatchEntryResult>(request.Entries.Count);
            foreach (RelayBatchEntry entry in request.Entries)
            {
                results.Add(await RunEntryAsync(entry, relay!.TenantID));
            }

            return Ok(new RelayBatchResponse
            {
                Results = results,
                RelayMode = serverConfig.RelayMode,
                RelayWaitWindowSeconds = serverConfig.RelayWaitWindowSeconds,
            });
        }

        private async Task<RelayBatchEntryResult> RunEntryAsync(RelayBatchEntry entry, int relayTenantId)
        {
            Device? device = await Repo.DeviceGetByApiIdAsync(entry.DeviceApiId);
            if (device is null || !DeviceAuth.ConstantTimeEquals(entry.DeviceApiKey, device.ApiKey))
            {
                return new RelayBatchEntryResult { Success = false, StatusCode = 401, Error = "Unknown device or apiKey mismatch." };
            }
            if (device.TenantID != relayTenantId)
            {
                // A relay belongs to one tenant - forwarding for another tenant's device would let a stolen/misconfigured relay cross tenant boundaries.
                return new RelayBatchEntryResult { Success = false, StatusCode = 403, Error = "Device belongs to a different tenant than this relay." };
            }

            try
            {
                return entry.Type switch
                {
                    RelayEntryType.Config => await RunConfigAsync(device, entry.Payload),
                    RelayEntryType.SensorData => await RunSensorDataAsync(device, entry.Payload),
                    RelayEntryType.Event => await RunEventAsync(device, entry.Payload),
                    RelayEntryType.CommandAck => await RunCommandAckAsync(device, entry.Payload),
                    _ => new RelayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unknown entry type: {entry.Type}" },
                };
            }
            catch (JsonException ex)
            {
                // A malformed entry must not take the rest of the batch down with it - same reason
                // SensorController.flushBufferedSensorData() drops a poison file instead of wedging.
                return new RelayBatchEntryResult { Success = false, StatusCode = 400, Error = "Malformed payload: " + ex.Message };
            }
        }

        /// <summary>Same steps as DeviceApiController.GetConfig, in the same order (diagnostics
        /// upsert before the version check, so a batched device still bumps LastSeenAt even when
        /// nothing changed).</summary>
        private async Task<RelayBatchEntryResult> RunConfigAsync(Device device, JsonElement payload)
        {
            DeviceConfigPoll poll = payload.Deserialize<DeviceConfigPoll>() ?? new DeviceConfigPoll();

            await Repo.DeviceDiagnosticUpsertAsync(device.IDDevice!.Value, device.TenantID, poll);

            if (await firmwareCatalog.NoteHeartbeatAsync(device, poll.FirmwareVersion, poll.Board))
            {
                device.FirmwareUpdate = false;
                device.FirmwareTargetVersion = null;
                await Repo.EventDevicePushAsync(device.IDDevice.Value, device.TenantID, DeviceEventType.FirmwareUpdated, "version=" + poll.FirmwareVersion);
            }

            PendingCommand? pendingCommand = await commandQueue.GetPendingCommandAsync(device.IDDevice.Value);
            if (poll.ConfigVersion == device.ConfigVersion && pendingCommand == null)
            {
                return new RelayBatchEntryResult { Success = true, StatusCode = 200 }; // up to date, nothing queued - mirrors GetConfig's empty-200
            }

            DeviceConfig config = await configBuilder.BuildAsync(device, pendingCommand, poll.Board);
            return new RelayBatchEntryResult { Success = true, StatusCode = 200, Config = config };
        }

        /// <summary>Same steps as SensorDataController.Post.</summary>
        private async Task<RelayBatchEntryResult> RunSensorDataAsync(Device device, JsonElement payload)
        {
            JsonArray jsonArray = JsonNode.Parse(payload.GetRawText()) as JsonArray
                ?? throw new JsonException("SensorData payload must be a JSON array.");

            await Repo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceUnitID, device.DeviceUnitZoneID);

            return new RelayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// <summary>Same steps as DeviceApiController.PushEvent.</summary>
        private async Task<RelayBatchEntryResult> RunEventAsync(Device device, JsonElement payload)
        {
            DeviceEventPush push = payload.Deserialize<DeviceEventPush>() ?? new DeviceEventPush();
            if (!Enum.TryParse<DeviceEventType>(push.EventType, ignoreCase: true, out var eventType))
            {
                return new RelayBatchEntryResult { Success = false, StatusCode = 400, Error = $"Unknown eventType: {push.EventType}" };
            }

            await Repo.EventDevicePushAsync(device.IDDevice!.Value, device.TenantID, eventType, push.Message);

            if (eventType == DeviceEventType.CommandExecuted && push.CommandId is int commandId)
            {
                await commandQueue.MarkExecutedAsync(commandId, device.IDDevice!.Value);
            }

            return new RelayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// <summary>Same steps as DeviceApiController.AckCommand.</summary>
        private async Task<RelayBatchEntryResult> RunCommandAckAsync(Device device, JsonElement payload)
        {
            CommandAckRequest ack = payload.Deserialize<CommandAckRequest>() ?? new CommandAckRequest();
            await commandQueue.AcknowledgeCommandAsync(ack.CommandId, device.IDDevice!.Value);
            return new RelayBatchEntryResult { Success = true, StatusCode = 200 };
        }

        /// <summary>What the OWNING relay itself fetches to build its DevEUI-&gt;apiId/apiKey
        /// forwarding cache (RelayProfile.LoRaGateway) - includes ApiKey, unlike the admin list
        /// below, since Relay must reconstruct each mapped device's own request.</summary>
        [HttpGet("DeviceMapping")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.ApiKeyPolicy)]
        public async Task<ActionResult<IList<RelayDeviceMapping>>> DeviceMappingGetMine()
        {
            var (relay, error) = await GetCallerRelayAsync();
            if (error != null)
            {
                return error;
            }
            return Ok(await Repo.RelayDeviceMappingsWithSecretsGetAsync(relay!.IDDevice!.Value));
        }

        // ---- admin (Relay Devices page) ---------------------------------------------------

        [HttpGet("All")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<IList<Device>>> RelaysGetAll() =>
            Ok(await Repo.RelayDevicesGetAllAsync());

        [HttpGet("DeviceMapping/All")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<IList<RelayDeviceMapping>>> DeviceMappingGetAll(int idRelayDevice) =>
            Ok(await Repo.RelayDeviceMappingsGetAsync(idRelayDevice));

        [HttpPost("DeviceMapping")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<bool>> DeviceMappingAdd([FromBody] RelayDeviceMapping value)
        {
            if (string.IsNullOrWhiteSpace(value.DevEUI) || value.IDRelayDevice is not int idRelay || value.IDDevice is not int idDevice)
            {
                return BadRequest("IDRelayDevice, DevEUI and IDDevice are required.");
            }
            bool added = await Repo.RelayDeviceMappingAddAsync(idRelay, value.DevEUI.Trim().ToUpperInvariant(), idDevice);
            return added ? Ok(true) : Conflict("That DevEUI is already mapped for this relay, or IDDevice does not exist.");
        }

        [HttpDelete("DeviceMapping")]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult<bool>> DeviceMappingDelete(int idRelayDeviceMapping, int idRelayDevice) =>
            Ok(await Repo.RelayDeviceMappingDeleteAsync(idRelayDeviceMapping, idRelayDevice));
    }
}
