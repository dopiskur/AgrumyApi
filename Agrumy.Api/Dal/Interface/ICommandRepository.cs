using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Command facet of the data layer (roadmap #34, #74 pattern): raw deviceCommand CRUD
    /// only - dedup, fan-out, and the FIFO "which one is next" business logic all live in
    /// CommandQueueService (Agrumy.Api/Commands), which sits above this facet the same way
    /// OfflineAlertEvaluator sits above IDeviceRepository (roadmap #40).</summary>
    public interface ICommandRepository
    {
        /// <summary>Every Pending/Acknowledged (i.e. still "active") command for this device whose
        /// ActionType matches - used for the per-(device, ActionType) dedup check. Expired-but-not-
        /// yet-marked rows are excluded by the ExpiresAt filter, not by Status alone - see
        /// CommandQueueService for why a stuck Acknowledged row must not permanently block re-issuing.</summary>
        Task<bool> HasActiveCommandAsync(int deviceId, CommandActionType actionType, DateTime utcNow);

        /// <summary>Creates one Pending command row and bumps the device's CommandVersion in the
        /// same call (one write, matching the rest of this codebase's "config write + version bump
        /// together" convention, e.g. EfRepository.Devices.Config.cs). Returns the new row's id.</summary>
        Task<int> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt);

        /// <summary>Every Pending command for this device, oldest first - CommandQueueService picks
        /// the first one that is not (yet) expired and lazily expires any that are, so this
        /// deliberately returns the raw candidate list rather than pre-filtering by expiry itself.</summary>
        Task<IList<DeviceCommand>> GetPendingCommandsAsync(int deviceId);

        Task<DeviceCommand?> GetCommandByIdAsync(int commandId);

        Task SetCommandStatusAsync(int commandId, CommandStatus status, DateTime? executedAt = null);
    }
}
