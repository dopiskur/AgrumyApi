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
    public class DeviceCommandApiController(IRepository repo, ICache cache, CommandQueueService commandQueue) : ApiControllerBase(repo, cache)
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

        /// Status of one previously-issued command - the roadmap #294 gap: IssueCommand's CreatedCommandIds had no way to check on afterward short of direct DB access.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("{idDeviceCommand}")]
        public async Task<ActionResult<DeviceCommand>> GetCommand(int idDeviceCommand)
        {
            DeviceCommand? command = await Repo.GetCommandByIdAsync(idDeviceCommand);
            if (command == null)
            {
                return NotFound();
            }
            var (_, error) = await EnsureOwnedDeviceAsync(command.DeviceID);
            return error ?? Ok(command);
        }

        private Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(int idDevice) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceGetByIdAsync(idDevice), d => d.TenantID, "Device", forWrite: true);

        private Task<(DeviceUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int idDeviceUnitZone) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone), z => z.TenantID, "Zone", forWrite: true);

        private Task<(DeviceUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int idDeviceUnit) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitGetByIdAsync(idDeviceUnit), u => u.TenantID, "Unit", forWrite: true);
    }
}
