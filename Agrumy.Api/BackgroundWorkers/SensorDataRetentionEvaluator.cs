using api.Dal;
using api.Dal.Interface;
using Microsoft.EntityFrameworkCore;

namespace api.BackgroundWorkers
{
    /// MariaDB/MySQL has no add_retention_policy equivalent, so this daily-DELETEs past the cutoff (no OPTIMIZE TABLE - too expensive to run unattended); no-ops on Postgres, where ApplyRetentionPolicyAsync handles it instead.
    public sealed class SensorDataRetentionEvaluator(
        AgrumyDbContext db, IServerConfigRepository serverConfigRepo, ISensorDataRepository sensorDataRepo)
    {
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            if (db.Database.IsNpgsql())
            {
                return;
            }

            var config = await serverConfigRepo.ServerConfigGetAsync(1);
            if (config.SensorDataRetentionDays is not > 0)
            {
                return;
            }

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-config.SensorDataRetentionDays.Value);
            await sensorDataRepo.PurgeOldSensorDataAsync(cutoffUtc, shrinkAfterPurge: false, ct);
        }
    }
}
