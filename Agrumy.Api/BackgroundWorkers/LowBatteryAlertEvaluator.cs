using api.Dal.Interface;
using api.Models;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>A device's latest battery reading crossing ServerConfig.BatteryLowThreshold fires one alert per low-battery streak, dead-zone-latched against BatteryLowHysteresis to avoid chattering at the boundary.</summary>
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

                // Never-reported battery is NOT "low" for alerting purposes - nothing to compare against threshold.
                if (d.Battery is not int battery)
                {
                    continue;
                }

                bool low = battery <= threshold;
                bool recovered = battery >= clearAt;

                if (!low)
                {
                    // Only clear once the reading is fully back OUT of the dead zone (>= clearAt), not merely "not low" (> threshold), or it could spuriously re-fire next tick.
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

                // Same eventDevice table/timeline as device-pushed events - server-detected, not device-pushed, same as Offline.
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
