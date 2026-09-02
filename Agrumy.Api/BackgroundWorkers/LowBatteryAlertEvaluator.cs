using api.Dal.Interface;
using api.Models;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>Roadmap #12 (feature) + #40 (background-worker pattern), same shape as
    /// OfflineAlertEvaluator: a device's latest telemetry battery reading (see
    /// LowBatteryAlertCandidate) crossing ServerConfig.BatteryLowThreshold fires one alert per
    /// low-battery streak, dead-zone-latched against BatteryLowHysteresis the same way #10's
    /// relay threshold+hysteresis avoids chattering right at the boundary. Kept separate from
    /// LowBatteryAlertBackgroundService so it is directly unit-testable with mocked repositories.</summary>
    public sealed class LowBatteryAlertEvaluator(
        IDeviceRepository deviceRepo, IUserRepository userRepo, IServerConfigRepository serverConfigRepo, INotificationDispatcher dispatcher)
    {
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            ServerConfig serverConfig = await serverConfigRepo.ServerConfigGetAsync(1);

            double threshold = serverConfig.BatteryLowThreshold ?? 20.0;
            double hysteresis = Math.Max(0.0, serverConfig.BatteryLowHysteresis ?? 5.0);
            double clearAt = threshold + hysteresis;

            var candidates = await deviceRepo.LowBatteryAlertCandidatesGetAsync();

            foreach (var d in candidates)
            {
                ct.ThrowIfCancellationRequested();

                // Never-reported battery (no sensorData row yet, or a sensor type that doesn't
                // report one) is NOT "low" for alerting purposes - same reasoning as
                // OfflineAlertEvaluator's null-LastSeenAt skip: nothing to compare against threshold.
                if (d.Battery is not int battery)
                {
                    continue;
                }

                bool low = battery <= threshold;
                bool recovered = battery >= clearAt;

                if (!low)
                {
                    // Only clear once the reading is fully back OUT of the dead zone (>= clearAt),
                    // not merely "not low" (> threshold) - a reading sitting between threshold and
                    // clearAt must not spuriously clear the mark and immediately re-fire next tick.
                    if (recovered && d.LowBatteryNotifiedAt is not null)
                    {
                        await deviceRepo.DeviceLowBatteryNotifiedSetAsync(d.IDDevice, null);
                    }
                    continue;
                }

                if (d.LowBatteryNotifiedAt is not null)
                {
                    continue; // already alerted for this ongoing low-battery streak - dedup
                }

                string deviceLabel = string.IsNullOrWhiteSpace(d.DeviceName) ? $"Device {d.IDDevice}" : d.DeviceName;

                // Same eventDevice table/timeline as device-pushed events (roadmap #28) - server-
                // detected, not device-pushed, same as Offline.
                await deviceRepo.EventDevicePushAsync(d.IDDevice, d.TenantID, DeviceEventType.LowBattery,
                    $"Battery at {battery}% (threshold {threshold:0.#}%)");

                var admins = await userRepo.TenantAdminsGetAsync(d.TenantID);
                foreach (var admin in admins)
                {
                    if (string.IsNullOrWhiteSpace(admin.Email))
                    {
                        continue;
                    }
                    var notification = new Notification(
                        Subject: $"Agrumy: {deviceLabel} battery is low ({battery}%)",
                        Body: $"{deviceLabel} last reported {battery}% battery, at or below the configured threshold of {threshold:0.#}%.",
                        Recipient: new NotificationRecipient(Email: admin.Email),
                        Severity: NotificationSeverity.Warning);
                    await dispatcher.DispatchAsync(notification, ct);
                }

                await deviceRepo.DeviceLowBatteryNotifiedSetAsync(d.IDDevice, DateTime.UtcNow);
            }
        }
    }
}
