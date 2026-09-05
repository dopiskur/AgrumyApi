using api.Dal.Interface;

namespace api.BackgroundWorkers
{
    /// deviceCommand has no other cleanup path (roadmap #294) - Pending/Acknowledged rows are never touched regardless of age, only terminal (Executed/Expired) ones age out.
    public sealed class DeviceCommandRetentionEvaluator(ICommandRepository commandRepo)
    {
        private const int RetentionDays = 30;

        public Task RunOnceAsync(CancellationToken ct = default) =>
            commandRepo.PurgeOldCommandsAsync(DateTime.UtcNow.AddDays(-RetentionDays), ct);
    }
}
