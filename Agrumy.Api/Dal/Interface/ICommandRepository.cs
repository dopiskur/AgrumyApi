using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Command facet of the data layer: raw deviceCommand CRUD only - dedup, fan-out, and
    /// the FIFO "which one is next" business logic live in CommandQueueService (Agrumy.Api/Commands),
    /// which sits above this facet the same way OfflineAlertEvaluator sits above IDeviceRepository.</summary>
    public interface ICommandRepository
    {
        /// <summary>Every Pending/Acknowledged (i.e. still "active") command for this device whose
        /// ActionType matches - used for the per-(device, ActionType) dedup check. Expired-but-not-
        /// yet-marked rows are excluded by the ExpiresAt filter, not by Status alone.</summary>
        Task<bool> HasActiveCommandAsync(int deviceId, CommandActionType actionType, DateTime utcNow);

        /// <summary>Creates one Pending command row and bumps the device's CommandVersion in the same call.</summary>
        Task<int> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt);

        /// <summary>Every Pending command for this device, oldest first - CommandQueueService picks
        /// the first one that is not (yet) expired and lazily expires any that are.</summary>
        Task<IList<DeviceCommand>> GetPendingCommandsAsync(int deviceId);

        Task<DeviceCommand?> GetCommandByIdAsync(int commandId);

        Task SetCommandStatusAsync(int commandId, CommandStatus status, DateTime? executedAt = null);
    }
}
