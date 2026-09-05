using api.Dal.Interface;

namespace api.BackgroundWorkers
{
    /// Only terminal (Executed/Expired) deviceCommand rows age out - Pending/Acknowledged rows are never purged regardless of age.
    public sealed class DeviceCommandRetentionEvaluator(ICommandRepository commandRepo)
    {
        private const int RetentionDays = 30;

        public Task RunOnceAsync(CancellationToken ct = default) =>
            commandRepo.PurgeOldCommandsAsync(DateTime.UtcNow.AddDays(-RetentionDays), ct);
    }
}
