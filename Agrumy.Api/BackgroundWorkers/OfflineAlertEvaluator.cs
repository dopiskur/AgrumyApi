using api.Dal.Interface;
using api.Models;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>The actual offline-detection/notification logic (roadmap #40 infra + #6 offline
    /// alert type, pulled forward since every dependency it needs - LastSeenAt via #7/#8,
    /// NotificationDispatcher via #6 - already exists). Kept separate from
    /// OfflineAlertBackgroundService so it is directly unit-testable with mocked repositories,
    /// with no PeriodicTimer/IHostedService plumbing in the way.</summary>
    public sealed class OfflineAlertEvaluator(IDeviceRepository deviceRepo, IUserRepository userRepo, INotificationDispatcher dispatcher)
    {
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            DateTime utcNow = DateTime.UtcNow;
            var candidates = await deviceRepo.OfflineAlertCandidatesGetAsync();

            foreach (var d in candidates)
            {
                ct.ThrowIfCancellationRequested();

                // Never-seen devices (fresh registration, not yet through its first config poll)
                // are NOT "offline" for alerting purposes - ComputeOnline treats a null LastSeenAt
                // as offline for the Fleet dashboard badge, which is fine for a passive badge, but
                // would mean every brand-new device fires an "offline" email before it ever had a
                // chance to connect. Alerting is opt-in to "was reachable, now is not."
                if (d.LastSeenAt is null)
                {
                    continue;
                }

                bool online = DeviceFleetStatus.ComputeOnline(d.LastSeenAt, d.SleepSeconds, utcNow);

                if (online)
                {
                    if (d.OfflineNotifiedAt is not null)
                    {
                        // Clears the mark so the NEXT offline streak (not this one - it just ended)
                        // alerts fresh instead of staying silent forever after the first incident.
                        await deviceRepo.DeviceOfflineNotifiedSetAsync(d.IDDevice, null);
                    }
                    continue;
                }

                if (d.OfflineNotifiedAt is not null)
                {
                    continue; // already alerted for this ongoing streak - dedup
                }

                string deviceLabel = string.IsNullOrWhiteSpace(d.DeviceName) ? $"Device {d.IDDevice}" : d.DeviceName;

                // Same eventDevice table/timeline as device-pushed events (roadmap #28) - this one
                // is server-detected, not device-pushed, but an admin reading a device's Events
                // page should see "went offline" alongside NoInternet/ConfigApplied/etc., not a
                // second, separate log they'd never think to check.
                await deviceRepo.EventDevicePushAsync(d.IDDevice, d.TenantID, DeviceEventType.Offline,
                    $"No contact since {d.LastSeenAt:u}");

                var admins = await userRepo.TenantAdminsGetAsync(d.TenantID);
                foreach (var admin in admins)
                {
                    if (string.IsNullOrWhiteSpace(admin.Email))
                    {
                        continue;
                    }
                    var notification = new Notification(
                        Subject: $"Agrumy: {deviceLabel} is offline",
                        Body: $"{deviceLabel} has not reported in since {d.LastSeenAt:u} UTC.",
                        Recipient: new NotificationRecipient(Email: admin.Email),
                        Severity: NotificationSeverity.Warning);
                    await dispatcher.DispatchAsync(notification, ct);
                }

                await deviceRepo.DeviceOfflineNotifiedSetAsync(d.IDDevice, utcNow);
            }
        }
    }
}
