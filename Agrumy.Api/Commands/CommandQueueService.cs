using System.Text.Json;
using api.Dal.Interface;
using api.Models;

namespace api.Commands
{
    /// Success (>=1 command created) allows Message to be null; AllDuplicates and TargetNotFound always carry one so the API controller can pass it straight through as the error body.
    public enum IssueCommandOutcome
    {
        Success,
        AllDuplicates,
        TargetNotFound,
    }

    public sealed record IssueCommandResult(IssueCommandOutcome Outcome, IReadOnlyList<int> CreatedCommandIds, string? Message = null);

    /// Dedup, target resolution/fan-out, FIFO pending-command lookup, and ack/execute state transitions; no background worker - expiry is lazy, applied the moment a stale Pending row is next looked at.
    public sealed class CommandQueueService(ICommandRepository commandRepo, IDeviceRepository deviceRepo, IDeviceUnitRepository unitRepo)
    {
        private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);

        /// Resolves TargetType/TargetId to the actual device(s), then per-device dedup against an active unexpired command of that ActionType; a fan-out is Success unless EVERY resolved device already had one, which is AllDuplicates.
        public async Task<IssueCommandResult> IssueCommandAsync(CommandTargetType targetType, int targetId, CommandActionType actionType)
        {
            IList<Device> targets;
            string notFoundMessage;
            switch (targetType)
            {
                case CommandTargetType.Device:
                    Device? device = await deviceRepo.DeviceGetByIdAsync(targetId);
                    targets = device == null ? [] : [device];
                    notFoundMessage = $"Device {targetId} not found.";
                    break;
                case CommandTargetType.Zone:
                    // A zone has at most one controller - null means it genuinely has none, which must surface as an error, not a silent zero-created no-op.
                    Device? controller = await unitRepo.DeviceUnitZoneGetControllerAsync(targetId);
                    targets = controller == null ? [] : [controller];
                    notFoundMessage = $"Zone {targetId} has no controller assigned.";
                    break;
                case CommandTargetType.Unit:
                    targets = await unitRepo.DeviceUnitGetControllersAsync(targetId);
                    notFoundMessage = $"Unit {targetId} has no controllers across any of its zones.";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null);
            }

            if (targets.Count == 0)
            {
                return new IssueCommandResult(IssueCommandOutcome.TargetNotFound, [], notFoundMessage);
            }

            return await IssueToTargetsAsync(targets, actionType);
        }

        /// Fans ScanForDevices to every sensor-only device in scope (zone, else unit, else tenant-wide) - a different target-resolution rule than IssueCommandAsync's, same dedup/fan-out tail via IssueToTargetsAsync.
        public async Task<IssueCommandResult> IssueScanCommandAsync(int? tenantId, int? unitId, int? zoneId)
        {
            IList<Device> targets;
            string notFoundMessage;
            if (zoneId is int zid)
            {
                targets = await unitRepo.DeviceUnitZoneGetSensorsAsync(zid);
                notFoundMessage = $"Zone {zid} has no sensor-only devices.";
            }
            else if (unitId is int uid)
            {
                targets = await unitRepo.DeviceUnitGetSensorsAsync(uid);
                notFoundMessage = $"Unit {uid} has no sensor-only devices across any of its zones.";
            }
            else
            {
                targets = await deviceRepo.DevicesSensorOnlyGetAsync(tenantId);
                notFoundMessage = "No sensor-only devices found.";
            }

            if (targets.Count == 0)
            {
                return new IssueCommandResult(IssueCommandOutcome.TargetNotFound, [], notFoundMessage);
            }

            return await IssueToTargetsAsync(targets, CommandActionType.ScanForDevices);
        }

        /// Issues a ProvisionDevice command to exactly one device (the roadmap #268 Register flow's
        /// winning scanning device), carrying payloadJson - see api.Models.DiscoveryProvisionPayload.
        public async Task<IssueCommandResult> IssueProvisionCommandAsync(int deviceId, string payloadJson)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (await commandRepo.HasActiveCommandAsync(deviceId, CommandActionType.ProvisionDevice, utcNow))
            {
                return new IssueCommandResult(IssueCommandOutcome.AllDuplicates, [], "A provisioning command is already pending for this device.");
            }
            DateTime expiresAt = utcNow + DefaultExpiry;
            if (await commandRepo.AddCommandAsync(deviceId, CommandActionType.ProvisionDevice, utcNow, expiresAt, payloadJson) is int newCommandId)
            {
                return new IssueCommandResult(IssueCommandOutcome.Success, [newCommandId]);
            }
            return new IssueCommandResult(IssueCommandOutcome.AllDuplicates, [], "A provisioning command is already pending for this device.");
        }

        /// Finds the active ProvisionDevice command whose payload targeted this MacAddress (the roadmap
        /// #268 Register call that led here) and marks it Executed so a later re-registration of the
        /// same mac never reapplies a stale intent. Null when this MacAddress was never discovered/
        /// registered through that flow - an ordinary #70 registration.
        public async Task<DiscoveryProvisionPayload?> ConsumePendingProvisionAsync(string macAddress)
        {
            foreach (var candidate in await commandRepo.GetActiveProvisionCommandsAsync())
            {
                if (candidate.Payload is null)
                {
                    continue;
                }
                DiscoveryProvisionPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<DiscoveryProvisionPayload>(candidate.Payload);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (payload != null && string.Equals(payload.DiscoveredApMac, macAddress, StringComparison.OrdinalIgnoreCase))
                {
                    await commandRepo.SetCommandStatusAsync(candidate.IDDeviceCommand, CommandStatus.Executed, DateTime.UtcNow);
                    return payload;
                }
            }
            return null;
        }

        /// Per-(device, ActionType) dedup then insert, shared by every fan-out entry point above.
        private async Task<IssueCommandResult> IssueToTargetsAsync(IList<Device> targets, CommandActionType actionType)
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime expiresAt = utcNow + DefaultExpiry;
            var created = new List<int>();

            foreach (var target in targets)
            {
                if (target.IDDevice is not int deviceId)
                {
                    continue;
                }
                if (await commandRepo.HasActiveCommandAsync(deviceId, actionType, utcNow))
                {
                    continue; // this one device is skipped, not the whole batch
                }
                // AddCommandAsync can still return null here - the DB unique index closes the race, this in-memory check is only a fast-path.
                if (await commandRepo.AddCommandAsync(deviceId, actionType, utcNow, expiresAt) is int newCommandId)
                {
                    created.Add(newCommandId);
                }
            }

            return created.Count > 0
                ? new IssueCommandResult(IssueCommandOutcome.Success, created)
                : new IssueCommandResult(IssueCommandOutcome.AllDuplicates, [], "A command of that type is already pending for the targeted device(s).");
        }

        /// The oldest non-expired Pending command for this device - lazily expires (and skips past) any that are, so a stuck expired command never hides a still-valid one of a different type.
        public async Task<PendingCommand?> GetPendingCommandAsync(int deviceId)
        {
            DateTime utcNow = DateTime.UtcNow;
            IList<DeviceCommand> candidates = await commandRepo.GetPendingCommandsAsync(deviceId); // oldest first

            foreach (var candidate in candidates)
            {
                if (candidate.ExpiresAt <= utcNow)
                {
                    await commandRepo.SetCommandStatusAsync(candidate.IDDeviceCommand, CommandStatus.Expired);
                    continue;
                }
                return new PendingCommand
                {
                    IDDeviceCommand = candidate.IDDeviceCommand,
                    ActionType = candidate.ActionType,
                    ExpiresAt = candidate.ExpiresAt,
                    Payload = candidate.Payload,
                };
            }
            return null;
        }

        /// Only a genuinely Pending command can be acknowledged; a command belonging to a different device is treated as not found.
        public async Task AcknowledgeCommandAsync(int commandId, int deviceId)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(commandId);
            if (command?.Status == CommandStatus.Pending && command.DeviceID == deviceId)
            {
                await commandRepo.SetCommandStatusAsync(commandId, CommandStatus.Acknowledged);
            }
        }

        /// Accepts either Pending or Acknowledged as the prior state - Pending covers Reboot, which has no "after" to ack from; same ownership check as AcknowledgeCommandAsync.
        public async Task MarkExecutedAsync(int commandId, int deviceId)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(commandId);
            if (command != null && command.Status is CommandStatus.Pending or CommandStatus.Acknowledged && command.DeviceID == deviceId)
            {
                await commandRepo.SetCommandStatusAsync(commandId, CommandStatus.Executed, DateTime.UtcNow);
            }
        }
    }
}
