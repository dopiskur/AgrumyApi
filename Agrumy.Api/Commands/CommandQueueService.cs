using api.Dal.Interface;
using api.Models;

namespace api.Commands
{
    /// <summary>Success (>=1 command created) allows Message to be null; AllDuplicates and TargetNotFound always carry one so the API controller can pass it straight through as the error body.</summary>
    public enum IssueCommandOutcome
    {
        Success,
        AllDuplicates,
        TargetNotFound,
    }

    public sealed record IssueCommandResult(IssueCommandOutcome Outcome, IReadOnlyList<int> CreatedCommandIds, string? Message = null);

    /// <summary>Dedup, target resolution/fan-out, FIFO pending-command lookup, and ack/execute state transitions; directly unit-testable with mocked repositories, no HTTP/controller plumbing. No background worker - expiry is lazy, applied the moment a stale Pending row is next looked at (GetPendingCommandAsync).</summary>
    public sealed class CommandQueueService(ICommandRepository commandRepo, IDeviceRepository deviceRepo, IDeviceUnitRepository unitRepo)
    {
        private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);

        /// <summary>Resolves TargetType/TargetId to the actual device(s) (Device: itself; Zone: its one controller; Unit: every controller across every zone under it), then per-device dedup (reject a new command whose ActionType already has an active, unexpired command for that device). A multi-device fan-out where some devices dedup and others don't is still Success with a shorter list - only "every resolved device already had one" is AllDuplicates.</summary>
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
                created.Add(await commandRepo.AddCommandAsync(deviceId, actionType, utcNow, expiresAt));
            }

            return created.Count > 0
                ? new IssueCommandResult(IssueCommandOutcome.Success, created)
                : new IssueCommandResult(IssueCommandOutcome.AllDuplicates, [], "A command of that type is already pending for the targeted device(s).");
        }

        /// <summary>The oldest Pending command for this device that is NOT expired - lazily expires (and skips past) any that are, so a stuck expired command of one ActionType never hides a still-valid one of a different type.</summary>
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
                };
            }
            return null;
        }

        /// <summary>Only a genuinely Pending command can be acknowledged; an unknown id or one already past Pending is silently ignored rather than erroring the device's otherwise-successful poll cycle over a redundant/late ack. deviceId is the authenticated caller (see DeviceApiController) - a command that resolves but belongs to a different device is treated the same as "not found" (roadmap #178, was an IDOR: any device could ack/execute any other device's command by guessing its id).</summary>
        public async Task AcknowledgeCommandAsync(int commandId, int deviceId)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(commandId);
            if (command?.Status == CommandStatus.Pending && command.DeviceID == deviceId)
            {
                await commandRepo.SetCommandStatusAsync(commandId, CommandStatus.Acknowledged);
            }
        }

        /// <summary>Accepts either Pending or Acknowledged as the prior state - Pending covers Reboot (which has no "after" to ack from; its first real confirmation IS the next poll succeeding at all) and any other action whose ack happened to be lost/delayed. deviceId ownership check per roadmap #178, same as AcknowledgeCommandAsync.</summary>
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
