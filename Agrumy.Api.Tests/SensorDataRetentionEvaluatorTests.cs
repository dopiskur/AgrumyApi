using api.BackgroundWorkers;
using api.Dal;
using api.Dal.Interface;
using api.Models;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>Exercises SensorDataRetentionEvaluator directly, no real database connection - the AgrumyDbContext below is never-connected since db.Database.IsNpgsql() only reads the provider the context was built with.</summary>
public class SensorDataRetentionEvaluatorTests
{
    private readonly Mock<IServerConfigRepository> _serverConfig = new(MockBehavior.Strict);
    private readonly Mock<ISensorDataRepository> _sensorData = new(MockBehavior.Strict);

    private SensorDataRetentionEvaluator NewEvaluator(DbProviderKind provider = DbProviderKind.MySql) =>
        new(new AgrumyDbContext(DbOptionsFactory.Build(provider, "server=unused;database=unused;")),
            _serverConfig.Object, _sensorData.Object);

    [Fact]
    public async Task Postgres_NoOps_NeverReadsServerConfig()
    {
        // Strict mocks: ServerConfigGetAsync/PurgeOldSensorDataAsync have no setup, proving the Postgres branch returned before touching either.
        await NewEvaluator(DbProviderKind.Postgres).RunOnceAsync();
    }

    [Fact]
    public async Task RetentionDaysNotSet_NoOps()
    {
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { SensorDataRetentionDays = null });

        await NewEvaluator().RunOnceAsync();

        // PurgeOldSensorDataAsync has no setup (Strict) - proves it was never called.

    }

    [Fact]
    public async Task RetentionDaysZero_NoOps()
    {
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { SensorDataRetentionDays = 0 });

        await NewEvaluator().RunOnceAsync();
    }

    [Fact]
    public async Task RetentionDaysPositive_PurgesWithoutShrink_AtCorrectCutoff()
    {
        _serverConfig.Setup(s => s.ServerConfigGetAsync(1)).ReturnsAsync(new ServerConfig { SensorDataRetentionDays = 30 });
        DateTime? capturedCutoff = null;
        bool? capturedShrink = null;
        _sensorData.Setup(s => s.PurgeOldSensorDataAsync(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, bool, CancellationToken>((cutoff, shrink, _) => { capturedCutoff = cutoff; capturedShrink = shrink; })
            .Returns(Task.CompletedTask);

        await NewEvaluator().RunOnceAsync();

        Assert.False(capturedShrink); // never auto-shrinks - roadmap #126's OPTIMIZE TABLE stays an explicit admin choice
        Assert.InRange(capturedCutoff!.Value, DateTime.UtcNow.AddDays(-30).AddMinutes(-1), DateTime.UtcNow.AddDays(-30).AddMinutes(1));
    }
}
