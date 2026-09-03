using api.Dal;
using api.Dal.Interface;
using Microsoft.EntityFrameworkCore;

namespace api.BackgroundWorkers
{
    /// <summary>Roadmap #15, MariaDB/MySQL side: this provider has no equivalent to TimescaleDB's
    /// add_retention_policy, so the #40 periodic-worker pattern does the same job directly - a
    /// daily DELETE WHERE DateCreated &lt; cutoff via ISensorDataRepository.PurgeOldSensorDataAsync
    /// (shrinkAfterPurge false: OPTIMIZE TABLE's full table rebuild is too expensive to run
    /// unattended every day, unlike the admin-triggered roadmap #126 purge). No-ops on Postgres -
    /// EfRepository.ApplyRetentionPolicyAsync is that provider's mechanism instead - and whenever
    /// ServerConfig.SensorDataRetentionDays is unset, same "admin must opt in" default as the rest
    /// of ServerConfig. Kept separate from SensorDataRetentionBackgroundService (same split as
    /// OfflineAlertEvaluator/LowBatteryAlertEvaluator) so it's testable without a running timer.</summary>
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
