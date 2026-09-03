using api.BackgroundWorkers;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>Exercises LowBatteryAlertEvaluator directly - no database, repositories/dispatcher are mocked.</summary>
public class LowBatteryAlertEvaluatorTests
{
    private readonly Mock<IDeviceRepository> _devices = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IServerConfigRepository> _serverConfig = new(MockBehavior.Strict);
    private readonly Mock<INotificationDispatcher> _dispatcher = new(MockBehavior.Strict);

    private LowBatteryAlertEvaluator NewEvaluator() => new(_devices.Object, _users.Object, _serverConfig.Object, _dispatcher.Object);

    private static LowBatteryAlertCandidate Candidate(
        int id = 1, int tenantId = 1, string? name = "Greenhouse Sensor",
        int? battery = null, DateTime? lowBatteryNotifiedAt = null) =>
        new(id, tenantId, name, battery, lowBatteryNotifiedAt);

    private void SetupCandidates(params LowBatteryAlertCandidate[] candidates) =>
        _devices.Setup(d => d.LowBatteryAlertCandidatesGetAsync()).ReturnsAsync(candidates);

    // Threshold=20, hysteresis=5 (defaults) unless a test overrides it - alert at <=20%, clear at >=25%.

    private void SetupServerConfig(double? threshold = 20.0, double? hysteresis = 5.0) =>
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1))
            .ReturnsAsync(new ServerConfig { BatteryLowThreshold = threshold, BatteryLowHysteresis = hysteresis });

    [Fact]
    public async Task Device_With_No_Battery_Reading_Is_Skipped_Not_Alerted()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(battery: null));

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: any unexpected call (EventDevicePushAsync, TenantAdminsGetAsync, DispatchAsync, DeviceLowBatteryNotifiedSetAsync) would throw during RunOnceAsync above.
    }

    [Fact]
    public async Task Healthy_Battery_With_No_Prior_Alert_Does_Nothing()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(battery: 80, lowBatteryNotifiedAt: null));

        await NewEvaluator().RunOnceAsync();
    }

    [Fact]
    public async Task Battery_Inside_DeadZone_After_Alert_Does_Not_Clear_The_Mark()
    {
        // threshold=20, hysteresis=5 -> clears only at >=25. 22 is above threshold but still in the dead zone - must stay latched, not clear.
        SetupServerConfig();
        SetupCandidates(Candidate(battery: 22, lowBatteryNotifiedAt: DateTime.UtcNow.AddHours(-1)));

        await NewEvaluator().RunOnceAsync();

        // Strict mock: DeviceLowBatteryNotifiedSetAsync must NOT be called from inside the dead zone.

    }

    [Fact]
    public async Task Battery_Recovered_Past_Hysteresis_Clears_The_Mark()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(battery: 25, lowBatteryNotifiedAt: DateTime.UtcNow.AddHours(-1)));
        _devices.Setup(d => d.DeviceLowBatteryNotifiedSetAsync(1, null)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _devices.Verify(d => d.DeviceLowBatteryNotifiedSetAsync(1, null), Times.Once);
    }

    [Fact]
    public async Task Newly_Low_Battery_Logs_Event_And_Notifies_Every_Admin_With_An_Email()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(tenantId: 7, battery: 15, lowBatteryNotifiedAt: null));

        _devices.Setup(d => d.EventDevicePushAsync(1, 7, DeviceEventType.LowBattery, It.IsAny<string>()))
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
        _devices.Setup(d => d.DeviceLowBatteryNotifiedSetAsync(1, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin1@example.com" && x.Subject.Contains("Greenhouse Sensor")),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin2@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _devices.Verify(d => d.DeviceLowBatteryNotifiedSetAsync(1, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Already_Alerted_Low_Battery_Device_Is_Not_Re_Notified()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(battery: 10, lowBatteryNotifiedAt: DateTime.UtcNow.AddMinutes(-10))); // still low, already alerted once

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: EventDevicePushAsync/TenantAdminsGetAsync/DispatchAsync would throw if called - dedup means none of them should be.
    }

    [Fact]
    public async Task Reading_Exactly_At_Threshold_Counts_As_Low()
    {
        SetupServerConfig(threshold: 20.0, hysteresis: 5.0);
        SetupCandidates(Candidate(tenantId: 7, battery: 20, lowBatteryNotifiedAt: null));

        _devices.Setup(d => d.EventDevicePushAsync(1, 7, DeviceEventType.LowBattery, It.IsAny<string>())).ReturnsAsync(true);
        _users.Setup(u => u.TenantAdminsGetAsync(7)).ReturnsAsync(new List<User>());
        _devices.Setup(d => d.DeviceLowBatteryNotifiedSetAsync(1, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _devices.Verify(d => d.DeviceLowBatteryNotifiedSetAsync(1, It.IsAny<DateTime>()), Times.Once);
    }
}
