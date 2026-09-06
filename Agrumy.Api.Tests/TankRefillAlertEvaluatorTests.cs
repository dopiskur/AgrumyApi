using api.BackgroundWorkers;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using Moq;

namespace Agrumy.Api.Tests;

/// Exercises TankRefillAlertEvaluator directly - no database, repositories/dispatcher are mocked. Fill percent math itself is covered by TankCalculatorTests.
public class TankRefillAlertEvaluatorTests
{
    private readonly Mock<IDeviceFarmUnitRepository> _deviceFarmUnits = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IServerConfigRepository> _serverConfig = new(MockBehavior.Strict);
    private readonly Mock<INotificationDispatcher> _dispatcher = new(MockBehavior.Strict);

    private TankRefillAlertEvaluator NewEvaluator() => new(_deviceFarmUnits.Object, _users.Object, _serverConfig.Object, _dispatcher.Object);

    // rawEmpty=0, rawFull=100 -> waterLevel IS the fill percent, keeps test math trivial.
    private static TankRefillAlertCandidate Candidate(
        int id = 1, int tenantId = 1, string? name = "Zone A",
        double? waterLevel = null, int? rawEmpty = 0, int? rawFull = 100, double? capacityLiters = 200,
        DateTime? tankRefillNotifiedAt = null) =>
        new(id, tenantId, name, waterLevel, rawEmpty, rawFull, capacityLiters, tankRefillNotifiedAt);

    private void SetupCandidates(params TankRefillAlertCandidate[] candidates) =>
        _deviceFarmUnits.Setup(d => d.TankRefillAlertCandidatesGetAsync()).ReturnsAsync(candidates);

    // Threshold=20, hysteresis=5 (defaults) unless a test overrides it - alert at <=20%, clear at >=25%.

    private void SetupServerConfig(double? threshold = 20.0, double? hysteresis = 5.0) =>
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1))
            .ReturnsAsync(new ServerConfig { TankRefillThreshold = threshold, TankRefillHysteresis = hysteresis });

    [Fact]
    public async Task Uncalibrated_Zone_Is_Skipped_Not_Alerted()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(rawEmpty: null, rawFull: null, capacityLiters: null));

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: any unexpected call (TenantAdminsGetAsync, DispatchAsync, TankRefillNotifiedSetAsync) would throw during RunOnceAsync above.
    }

    [Fact]
    public async Task Zone_With_No_Reading_Yet_Is_Skipped_Not_Alerted()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(waterLevel: null));

        await NewEvaluator().RunOnceAsync();
    }

    [Fact]
    public async Task Healthy_Fill_With_No_Prior_Alert_Does_Nothing()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(waterLevel: 80, tankRefillNotifiedAt: null));

        await NewEvaluator().RunOnceAsync();
    }

    [Fact]
    public async Task Fill_Inside_DeadZone_After_Alert_Does_Not_Clear_The_Mark()
    {
        // threshold=20, hysteresis=5 -> clears only at >=25. 22 is above threshold but still in the dead zone - must stay latched, not clear.
        SetupServerConfig();
        SetupCandidates(Candidate(waterLevel: 22, tankRefillNotifiedAt: DateTime.UtcNow.AddHours(-1)));

        await NewEvaluator().RunOnceAsync();

        // Strict mock: TankRefillNotifiedSetAsync must NOT be called from inside the dead zone.
    }

    [Fact]
    public async Task Fill_Recovered_Past_Hysteresis_Clears_The_Mark()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(waterLevel: 25, tankRefillNotifiedAt: DateTime.UtcNow.AddHours(-1)));
        _deviceFarmUnits.Setup(d => d.TankRefillNotifiedSetAsync(1, null)).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _deviceFarmUnits.Verify(d => d.TankRefillNotifiedSetAsync(1, null), Times.Once);
    }

    [Fact]
    public async Task Newly_Low_Tank_Notifies_Every_Admin_With_An_Email()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(tenantId: 7, waterLevel: 15, tankRefillNotifiedAt: null));

        _users.Setup(u => u.TenantAdminsGetAsync(7))
              .ReturnsAsync(new List<User>
              {
                  new() { IDUser = 1, Email = "admin1@example.com" },
                  new() { IDUser = 2, Email = "admin2@example.com" },
                  new() { IDUser = 3, Email = null }, // no email - must be skipped, not throw
              });
        _dispatcher.Setup(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<ChannelOutcome>());
        _deviceFarmUnits.Setup(d => d.TankRefillNotifiedSetAsync(1, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin1@example.com" && x.Subject.Contains("Zone A")),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(
            It.Is<Notification>(x => x.Recipient.Email == "admin2@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatcher.Verify(n => n.DispatchAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _deviceFarmUnits.Verify(d => d.TankRefillNotifiedSetAsync(1, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Already_Alerted_Low_Tank_Is_Not_Re_Notified()
    {
        SetupServerConfig();
        SetupCandidates(Candidate(waterLevel: 10, tankRefillNotifiedAt: DateTime.UtcNow.AddMinutes(-10))); // still low, already alerted once

        await NewEvaluator().RunOnceAsync();

        // Strict mocks: TenantAdminsGetAsync/DispatchAsync would throw if called - dedup means neither should be.
    }

    [Fact]
    public async Task Reading_Exactly_At_Threshold_Counts_As_Low()
    {
        SetupServerConfig(threshold: 20.0, hysteresis: 5.0);
        SetupCandidates(Candidate(tenantId: 7, waterLevel: 20, tankRefillNotifiedAt: null));

        _users.Setup(u => u.TenantAdminsGetAsync(7)).ReturnsAsync(new List<User>());
        _deviceFarmUnits.Setup(d => d.TankRefillNotifiedSetAsync(1, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        _deviceFarmUnits.Verify(d => d.TankRefillNotifiedSetAsync(1, It.IsAny<DateTime>()), Times.Once);
    }
}
