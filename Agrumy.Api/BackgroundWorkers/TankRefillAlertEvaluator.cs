using api.Dal.Interface;
using api.Models;
using api.Notifications;
using api.Utils;

namespace api.BackgroundWorkers
{
    /// A calibrated zone's fill percent (api.Utils.TankCalculator) crossing ServerConfig.TankRefillThreshold fires one alert per low-tank streak, dead-zone-latched against TankRefillHysteresis - same shape as LowBatteryAlertEvaluator, scoped to zones instead of devices.
    public sealed class TankRefillAlertEvaluator(
        IDeviceUnitRepository deviceUnitRepo, IUserRepository userRepo, IServerConfigRepository serverConfigRepo, INotificationDispatcher dispatcher)
    {
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            ServerConfig serverConfig = await serverConfigRepo.ServerConfigGetAsync(1);

            double threshold = serverConfig.TankRefillThreshold ?? 20.0;
            double hysteresis = Math.Max(0.0, serverConfig.TankRefillHysteresis ?? 5.0);
            double clearAt = threshold + hysteresis;

            var candidates = await deviceUnitRepo.TankRefillAlertCandidatesGetAsync();

            foreach (var z in candidates)
            {
                ct.ThrowIfCancellationRequested();

                (double? fillPercent, _) = TankCalculator.Compute(z.WaterLevel, z.WaterLevelRawEmpty, z.WaterLevelRawFull, z.TankCapacityLiters);
                // No reading yet for this zone's calibration is NOT "low" for alerting purposes - nothing to compare against threshold.
                if (fillPercent is not double fill)
                {
                    continue;
                }

                bool low = fill <= threshold;
                bool recovered = fill >= clearAt;

                if (!low)
                {
                    // Only clear once the reading is fully back OUT of the dead zone (>= clearAt), not merely "not low" (> threshold), or it could spuriously re-fire next tick.
                    if (recovered && z.TankRefillNotifiedAt is not null)
                    {
                        await deviceUnitRepo.TankRefillNotifiedSetAsync(z.IDDeviceUnitZone, null);
                    }
                    continue;
                }

                if (z.TankRefillNotifiedAt is not null)
                {
                    continue; // already alerted for this ongoing low-tank streak - dedup
                }

                string zoneLabel = string.IsNullOrWhiteSpace(z.DeviceUnitZoneName) ? $"Zone {z.IDDeviceUnitZone}" : z.DeviceUnitZoneName;

                var admins = await userRepo.TenantAdminsGetAsync(z.TenantID);
                foreach (var admin in admins)
                {
                    if (string.IsNullOrWhiteSpace(admin.Email))
                    {
                        continue;
                    }
                    var notification = new Notification(
                        Subject: $"Agrumy: {zoneLabel} tank needs a refill ({fill:0.#}%)",
                        Body: $"{zoneLabel}'s tank is at {fill:0.#}%, at or below the configured threshold of {threshold:0.#}%.",
                        Recipient: new NotificationRecipient(Email: admin.Email),
                        Severity: NotificationSeverity.Warning);
                    await dispatcher.DispatchAsync(notification, ct);
                }

                await deviceUnitRepo.TankRefillNotifiedSetAsync(z.IDDeviceUnitZone, DateTime.UtcNow);
            }
        }
    }
}
