using api.Models;

namespace api.Dal.Interface
{
    /// Command facet: raw deviceCommand CRUD only - dedup, fan-out, and FIFO ordering live in CommandQueueService above this facet.
    public interface ICommandRepository
    {
        /// Every still-active (Pending/Acknowledged) command for this device matching ActionType, used for the per-(device, ActionType) dedup check - excludes expired-but-not-yet-marked rows via the ExpiresAt filter, not Status alone.
        Task<bool> HasActiveCommandAsync(int deviceId, CommandActionType actionType, DateTime utcNow);

        /// Creates one Pending command row and bumps the device's CommandVersion in the same call - null return means a unique-constraint rejection (another request already has an active command for this pair), treated by the caller as a dedup skip. payload is only ever set for ProvisionDevice (api.Models.DiscoveryProvisionPayload, JSON).
        Task<int?> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt, string? payload = null);

        /// Every Pending command for this device, oldest first - CommandQueueService picks the first not (yet) expired and lazily expires any that are.
        Task<IList<DeviceCommand>> GetPendingCommandsAsync(int deviceId);

        Task<DeviceCommand?> GetCommandByIdAsync(int commandId);

        Task SetCommandStatusAsync(int commandId, CommandStatus status, DateTime? executedAt = null);
    }
}
