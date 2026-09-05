using api.BackgroundWorkers;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using Moq;

namespace Agrumy.Api.Tests;

/// Exercises OfflineAlertEvaluator directly - no database, repositories/dispatcher are mocked.
public class OfflineAlertEvaluatorTests
{
    private readonly Mock<IDeviceRepository> _devices = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<INotificationDispatcher> _dispatcher = new(MockBehavior.Strict);

    private OfflineAlertEvaluator NewEvaluator() => new(_devices.Object, _users.Object, _dispatcher.Object);

    private static OfflineAlertCandidate Candidate(
        int id = 1, int tenantId = 1, string? name = "Greenhouse Sensor",
        int? sleepSeconds = 60, DateTime? lastSeenAt = null, DateTime? offlineNotifiedAt = null) =>
        new(id, tenantId, name, sleepSeconds, lastSeenAt, offlineNotifiedAt);

    private void SetupCandidates(params OfflineAlertCandidate[] candidates) =>
        _devices.Setup(d => d.OfflineAlertCandidatesGetAsync()).ReturnsAsync(candidates);

    [Fact]
    public async Task NeverSeen_Device_Is_Skipped_Not_Alerted()
    {
        // LastSeenAt null = fresh registration, not the same thing as "was reachable, now is not".
        SetupCandidates(Candidate(lastSeenAt: null));

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: any unexpected call (EventDevicePushAsync, TenantAdminsGetAsync, DispatchAsync, DeviceOfflineNotifiedSetAsync) would throw during RunOnceAsync above.
    }

    [Fact]
    public async Task Online_Device_With_No_Prior_Alert_Does_Nothing()
    {
        SetupCandidates(Candidate(lastSeenAt: DateTime.UtcNow, offlineNotifiedAt: null));

        await NewEvaluator().RunOnceAsync();
    }

    [Fact]
    public async Task Online_Device_That_Was_Previously_Alerted_Clears_The_Mark()
    {
        SetupCandidates(Candidate(lastSeenAt: DateTime.UtcNow, offlineNotifiedAt: DateTime.UtcNow.AddHours(-1)));
        _devices.Setup(d => d.DeviceOfflineNotifiedSetAsync(1, null)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _devices.Verify(d => d.DeviceOfflineNotifiedSetAsync(1, null), Times.Once);
    }

    [Fact]
    public async Task Newly_Offline_Device_Logs_Event_And_Notifies_Every_Admin_With_An_Email()
    {
        DateTime lastSeen = DateTime.UtcNow.AddHours(-1); // well past the ~4.5 min ComputeOnline window
        SetupCandidates(Candidate(tenantId: 7, lastSeenAt: lastSeen, offlineNotifiedAt: null));

        _devices.Setup(d => d.EventDevicePushAsync(1, 7, DeviceEventType.Offline, It.IsAny<string>()))
                .ReturnsAsync(true);
        _users.Setup(u => u.TenantAdminsGetAsync(7))
              .ReturnsAsync(new List<User>
              {
                  new() { IDUser = 1, Email = "admin1@example.com" },
                  new() { IDUser = 2, Email = "admin2@example.com" },
                  new() { IDUser = 3, Email = null }, // no email - must be skipped, not throw
              });
        _dispatcher.Setup(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ChannelOutcome>());
        _devices.Setup(d => d.DeviceOfflineNotifiedSetAsync(1, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin1@example.com" && x.Subject.Contains("Greenhouse Sensor")),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin2@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _devices.Verify(d => d.DeviceOfflineNotifiedSetAsync(1, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Already_Alerted_Offline_Device_Is_Not_Re_Notified()
    {
        SetupCandidates(Candidate(
            lastSeenAt: DateTime.UtcNow.AddHours(-1),
            offlineNotifiedAt: DateTime.UtcNow.AddMinutes(-10))); // still offline, already alerted once

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: EventDevicePushAsync/TenantAdminsGetAsync/DispatchAsync would throw if called - dedup means none of them should be.
    }
}
