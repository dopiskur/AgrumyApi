using api.Dal;
using api.Dal.Interface;
using Microsoft.EntityFrameworkCore;

namespace api.BackgroundWorkers
{
    /// <summary>MariaDB/MySQL has no equivalent to TimescaleDB's add_retention_policy, so this does a daily DELETE WHERE DateCreated &lt; cutoff (shrinkAfterPurge false: OPTIMIZE TABLE's full rebuild is too expensive to run unattended daily). No-ops on Postgres, where ApplyRetentionPolicyAsync handles it instead.</summary>
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
