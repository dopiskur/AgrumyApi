using api.Commands;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Issues a device command, resolved/fanned-out server-side by CommandQueueService. Ownership checks mirror DeviceApiController/DeviceUnitApiController (duplicated here, not shared, per this codebase's existing per-controller convention).</summary>
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
            return result.Outcome switch
            {
                IssueCommandOutcome.Success => Ok(result.CreatedCommandIds),
                IssueCommandOutcome.AllDuplicates => Conflict(result.Message),
                IssueCommandOutcome.TargetNotFound => NotFound(result.Message),
                _ => StatusCode(500),
            };
        }

        private async Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(int idDevice)
        {
            Device? device = await Repo.DeviceGetByIdAsync(idDevice);
            if (device is null)
            {
                return (null, NotFound());
            }
            if (device.TenantID != CallerTenantId && !CallerManagesDevicesGlobally)
            {
                return (device, StatusCode(403, "Device belongs to a different tenant"));
            }
            return (device, null);
        }

        private async Task<(DeviceUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int idDeviceUnitZone)
        {
            DeviceUnitZone? zone = await Repo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone);
            if (zone is null)
            {
                return (null, NotFound());
            }
            if (zone.TenantID != CallerTenantId && !CallerManagesDevicesGlobally)
            {
                return (zone, StatusCode(403, "Zone belongs to a different tenant"));
            }
            return (zone, null);
        }

        private async Task<(DeviceUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int idDeviceUnit)
        {
            DeviceUnit? unit = await Repo.DeviceUnitGetByIdAsync(idDeviceUnit);
            if (unit is null)
            {
                return (null, NotFound());
            }
            if (unit.TenantID != CallerTenantId && !CallerManagesDevicesGlobally)
            {
                return (unit, StatusCode(403, "Unit belongs to a different tenant"));
            }
            return (unit, null);
        }
    }
}
