using api.Dal.Interface;
using api.Models;
using api.Notifications;

namespace api.BackgroundWorkers
{
    /// <summary>Offline-detection/notification logic, kept separate from OfflineAlertBackgroundService so it is directly unit-testable with mocked repositories.</summary>
    public sealed class OfflineAlertEvaluator(IDeviceRepository deviceRepo, IUserRepository userRepo, INotificationDispatcher dispatcher)
    {
        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            DateTime utcNow = DateTime.UtcNow;
            var candidates = await deviceRepo.OfflineAlertCandidatesGetAsync();

            foreach (var d in candidates)
            {
                ct.ThrowIfCancellationRequested();

                // Never-seen devices are NOT "offline" for alerting purposes, unlike the Fleet dashboard badge - alerting is opt-in to "was reachable, now is not."
                if (d.LastSeenAt is null)
                {
                    continue;
                }

                bool online = DeviceFleetStatus.ComputeOnline(d.LastSeenAt, d.SleepSeconds, utcNow);

                if (online)
                {
                    if (d.OfflineNotifiedAt is not null)
                    {
                        // Clears the mark so the NEXT offline streak alerts fresh instead of staying silent forever after the first incident.
                        await deviceRepo.DeviceOfflineNotifiedSetAsync(d.IDDevice, null);
                    }
                    continue;
                }

                if (d.OfflineNotifiedAt is not null)
                {
                    continue; // already alerted for this ongoing streak - dedup
                }

                string deviceLabel = string.IsNullOrWhiteSpace(d.DeviceName) ? $"Device {d.IDDevice}" : d.DeviceName;

                // Same eventDevice table/timeline as device-pushed events - server-detected, not device-pushed, but should appear alongside them on the Events page.
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
