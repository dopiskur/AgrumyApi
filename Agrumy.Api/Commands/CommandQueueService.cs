using api.Dal.Interface;
using api.Models;

namespace api.Commands
{
    /// <summary>Roadmap #34: how a caller finds out what IssueCommandAsync actually did. Success
    /// (>=1 command created) still allows Message to be null; AllDuplicates and TargetNotFound
    /// always carry one so the API controller can pass it straight through as the error body.</summary>
    public enum IssueCommandOutcome
    {
        Success,
        AllDuplicates,
        TargetNotFound,
    }

    public sealed record IssueCommandResult(IssueCommandOutcome Outcome, IReadOnlyList<int> CreatedCommandIds, string? Message = null);

    /// <summary>Roadmap #34's business logic - dedup, target resolution/fan-out, FIFO pending-
    /// command lookup, and the ack/execute state transitions. Kept separate from
    /// DeviceCommandApiController/DeviceApiController the same way OfflineAlertEvaluator is kept
    /// separate from OfflineAlertBackgroundService (roadmap #40) - directly unit-testable with
    /// mocked repositories, no HTTP/controller plumbing in the way. No background worker: expiry
    /// is lazy, applied the moment a stale Pending row is next looked at
    /// (GetPendingCommandAsync) - preferred over a periodic sweep per the roadmap instructions
    /// ("manje pokretnih dijelova").</summary>
    public sealed class CommandQueueService(ICommandRepository commandRepo, IDeviceRepository deviceRepo, IDeviceUnitRepository unitRepo)
    {
        // Not specified numerically in the roadmap design - these are meant to be discrete,
        // "right now" actions (reboot/OTA-check/config-resync), not something worth honoring an
        // hour later once the operator's context has likely moved on. 30 minutes is a deliberately
        // short window; revisit if a real operational need for something longer shows up.
        private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);

        /// <summary>Resolves TargetType/TargetId to the actual device(s) (Device: itself; Zone: its
        /// one controller, #82's own "at most one" invariant; Unit: every controller across every
        /// zone under it, zones with none simply absent - not an error, unlike a Zone target with
        /// none), then per-device dedup (roadmap #34 PROBLEM 2: reject a NEW command whose
        /// ActionType already has an active - Pending or Acknowledged, unexpired - command for that
        /// device) before creating one Command row per surviving device. A multi-device fan-out
        /// where SOME devices dedup and others don't is Success with a shorter list, not an error -
        /// only "every resolved device already had one" is AllDuplicates.</summary>
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
                    // #82 rule (a): a zone has at most one controller - null means it genuinely has
                    // none, which must surface as an error, not a silent zero-created no-op.
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
                    continue; // this one device is skipped, not the whole batch - see roadmap #34's own 3-zone/1-already-pending example
                }
                created.Add(await commandRepo.AddCommandAsync(deviceId, actionType, utcNow, expiresAt));
            }

            return created.Count > 0
                ? new IssueCommandResult(IssueCommandOutcome.Success, created)
                : new IssueCommandResult(IssueCommandOutcome.AllDuplicates, [], "A command of that type is already pending for the targeted device(s).");
        }

        /// <summary>The oldest Pending command for this device that is NOT expired - lazily expires
        /// (and skips past) any that are, rather than stopping at the first one found, so a stuck
        /// expired command of one ActionType never hides a still-valid one of a different type.
        /// Null if there is nothing left to do.</summary>
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

        /// <summary>Roadmap #34: the device confirms receipt BEFORE executing (see
        /// api.Controllers.API.DeviceApiController's Command/Ack endpoint) - only a genuinely
        /// Pending command can be acknowledged; an unknown id or one already past Pending
        /// (Acknowledged/Executed/Expired) is silently ignored rather than erroring the device's
        /// otherwise-successful poll cycle over a redundant/late ack.</summary>
        public async Task AcknowledgeCommandAsync(int commandId)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(commandId);
            if (command?.Status == CommandStatus.Pending)
            {
                await commandRepo.SetCommandStatusAsync(commandId, CommandStatus.Acknowledged);
            }
        }

        /// <summary>Roadmap #34: called from PushEvent when the device reports a CommandExecuted
        /// event with a CommandId. Accepts either Pending or Acknowledged as the prior state -
        /// Pending covers Reboot (which has no "after" to ack from on the same connection; its
        /// first real confirmation IS the next poll succeeding at all) and any other action whose
        /// ack call happened to be lost/delayed.</summary>
        public async Task MarkExecutedAsync(int commandId)
        {
            DeviceCommand? command = await commandRepo.GetCommandByIdAsync(commandId);
            if (command != null && command.Status is CommandStatus.Pending or CommandStatus.Acknowledged)
            {
                await commandRepo.SetCommandStatusAsync(commandId, CommandStatus.Executed, DateTime.UtcNow);
            }
        }
    }
}
