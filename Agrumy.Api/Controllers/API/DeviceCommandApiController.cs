using System.Text.Json;
using System.Text.Json.Nodes;
using api.Commands;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// Issues a device command, resolved/fanned-out server-side by CommandQueueService - ownership checks reuse ApiControllerBase.EnsureOwnedDeviceEntityAsync, always as a write since issuing a command is never a read-only action.
    [Route("/api/DeviceCommand")]
    public class DeviceCommandApiController(ICommandRepository commandRepo, IDeviceRepository deviceRepo, IDeviceFarmUnitRepository deviceFarmUnitRepo, IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache, CommandQueueService commandQueue) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        public async Task<ActionResult<IReadOnlyList<int>>> IssueCommand([FromBody] IssueCommandRequest request)
        {
            ActionResult? ownershipError = request.TargetType switch
            {
                CommandTargetType.Device => (await EnsureOwnedDeviceAsync(request.TargetId)).Error,
                CommandTargetType.Zone => (await EnsureOwnedZoneAsync(request.TargetId)).Error,
                CommandTargetType.Unit => (await EnsureOwnedUnitAsync(request.TargetId)).Error,
                _ => BadRequest($"Unknown targetType: {request.TargetType}"),
            };
            if (ownershipError != null)
            {
                return ownershipError;
            }

            IssueCommandResult result = await commandQueue.IssueCommandAsync(request.TargetType, request.TargetId, request.ActionType);
            if (result.Outcome == IssueCommandOutcome.Success)
            {
                await WriteAuditAsync("DeviceCommand.Issued", CallerTenantId, request.TargetType.ToString(), request.TargetId.ToString(), request.ActionType.ToString());
            }
            return result.Outcome switch
            {
                IssueCommandOutcome.Success => Ok(result.CreatedCommandIds),
                IssueCommandOutcome.AllDuplicates => Conflict(result.Message),
                IssueCommandOutcome.TargetNotFound => NotFound(result.Message),
                _ => StatusCode(500),
            };
        }

        /// Status of one previously-issued command - IssueCommand's CreatedCommandIds otherwise had no way to check on afterward short of direct DB access.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("{idDeviceCommand}")]
        public async Task<ActionResult<DeviceCommand>> GetCommand(int idDeviceCommand)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(idDeviceCommand);
            if (command == null)
            {
                return NotFound();
            }
            var (_, error) = await EnsureOwnedDeviceAsync(command.DeviceID);
            if (error != null)
            {
                return error;
            }
            if (MaskSensitivePayload(command.Payload) is string masked)
            {
                command.Payload = masked;
            }
            return Ok(command);
        }

        /// ProvisionDevice/UpdateWifiCredentials payloads carry a WiFi password, registration PIN, and username in plaintext (see api.Models.DiscoveryProvisionPayload/WifiUpdatePayload) - fully redacted here, not partially masked like ServiceController::maskSecret does for apiKey, since a password must never have any real character visible. Null on a non-JSON or non-sensitive payload (plain Reboot/ForceOTA commands carry none) so those pass through unchanged.
        private static string? MaskSensitivePayload(string? payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }
            JsonObject? node;
            try
            {
                node = JsonNode.Parse(payload) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
            if (node == null)
            {
                return null;
            }
            bool redacted = false;
            foreach (string sensitiveKey in new[] { "WifiPassword", "Pin", "Username" })
            {
                if (node.ContainsKey(sensitiveKey))
                {
                    node[sensitiveKey] = "[REDACTED]";
                    redacted = true;
                }
            }
            return redacted ? node.ToJsonString() : null;
        }

        private Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(int idDevice) =>
            EnsureOwnedDeviceEntityAsync(() => deviceRepo.DeviceGetByIdAsync(idDevice), d => d.TenantID, "Device", forWrite: true);

        private Task<(DeviceFarmUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int idDeviceFarmUnitZone) =>
            EnsureOwnedDeviceEntityAsync(() => deviceFarmUnitRepo.DeviceFarmUnitZoneGetByIdAsync(idDeviceFarmUnitZone), z => z.TenantID, "Zone", forWrite: true);

        private Task<(DeviceFarmUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int idDeviceFarmUnit) =>
            EnsureOwnedDeviceEntityAsync(() => deviceFarmUnitRepo.DeviceFarmUnitGetByIdAsync(idDeviceFarmUnit), u => u.TenantID, "Unit", forWrite: true);
    }
}
