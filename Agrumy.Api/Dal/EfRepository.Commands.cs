using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// ICommandRepository members - forwarded to the standalone EfCommandRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<bool> HasActiveCommandAsync(int deviceId, CommandActionType actionType, DateTime utcNow) =>
            commandRepository.HasActiveCommandAsync(deviceId, actionType, utcNow);

        public Task<int?> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt, string? payload = null) =>
            commandRepository.AddCommandAsync(deviceId, actionType, issuedAt, expiresAt, payload);

        public Task<IList<DeviceCommand>> GetPendingCommandsAsync(int deviceId) => commandRepository.GetPendingCommandsAsync(deviceId);

        public Task ExpirePendingCommandsAsync(int deviceId, DateTime utcNow) => commandRepository.ExpirePendingCommandsAsync(deviceId, utcNow);

        public Task<IList<DeviceCommand>> GetActiveProvisionCommandsAsync() => commandRepository.GetActiveProvisionCommandsAsync();

        public Task<DeviceCommand?> GetCommandByIdAsync(int commandId) => commandRepository.GetCommandByIdAsync(commandId);

        public Task SetCommandStatusAsync(int commandId, CommandStatus status, DateTime? executedAt = null) =>
            commandRepository.SetCommandStatusAsync(commandId, status, executedAt);

        public Task PurgeOldCommandsAsync(DateTime issuedBeforeUtc, CancellationToken ct = default) => commandRepository.PurgeOldCommandsAsync(issuedBeforeUtc, ct);
    }
}
