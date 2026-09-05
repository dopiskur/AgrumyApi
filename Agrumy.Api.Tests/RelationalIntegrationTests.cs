using System.Text.Json;
using System.Text.Json.Nodes;
using api;
using api.Dal;
using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agrumy.Api.Tests;

/// Runs against every real database engine configured via AGRUMY_TEST_MYSQL/AGRUMY_TEST_POSTGRES; skipped when unset.
public sealed class RelationalIntegrationFixture
{
    public sealed record Target(DbProviderKind Provider, string ConnectionString, int DeviceTypeId);

    private readonly Dictionary<DbProviderKind, Target> _targets = new();
    public IReadOnlyCollection<Target> Targets => _targets.Values;

    public RelationalIntegrationFixture()
    {
        TryInit(DbProviderKind.MySql, Environment.GetEnvironmentVariable("AGRUMY_TEST_MYSQL"));
        TryInit(DbProviderKind.Postgres, Environment.GetEnvironmentVariable("AGRUMY_TEST_POSTGRES"));
    }

    private void TryInit(DbProviderKind provider, string? conn)
    {
        if (string.IsNullOrWhiteSpace(conn)) return;

        using var db = new AgrumyDbContext(DbOptionsFactory.Build(provider, conn));
        db.Database.EnsureCreated();

        if (!db.UserRoles.Any(r => r.RoleName == RoleNames.TenantReader))
        {
            db.UserRoles.AddRange(RoleNames.All.Select(name => new UserRoleRow { RoleName = name }));
            db.SaveChanges();
        }

        int deviceType = db.DeviceTypes.Where(t => t.DeviceTypeName == "greenhouse")
                           .Select(t => (int?)t.IDDeviceType).FirstOrDefault() ?? SeedDeviceType(db);

        // deviceUnitZone.DeviceUnitID has a real FK to deviceUnit - the sentinel Zone row below (DeviceUnitID=0) needs the sentinel Unit row to already exist.
        if (!db.DeviceUnits.Any())
            db.DeviceUnits.Add(new DeviceUnitRow { IDDeviceUnit = 0, TenantID = null, DeviceUnitName = "Default" });
        db.SaveChanges();
        if (!db.DeviceUnitZones.Any())
            db.DeviceUnitZones.Add(new DeviceUnitZoneRow { IDDeviceUnitZone = 0, TenantID = null, DeviceUnitID = 0, DeviceUnitZoneName = "Disabled" });

        if (!db.DeviceTypeServices.Any())
            db.DeviceTypeServices.Add(new DeviceTypeServiceRow { IDDeviceTypeService = 1, ServiceType = "HTTPS" });
        if (!db.DeviceTypeRelays.Any())
            db.DeviceTypeRelays.AddRange(
                new DeviceTypeRelayRow { IDDeviceTypeRelay = 0, RelayName = "Disabled" },
                new DeviceTypeRelayRow { IDDeviceTypeRelay = 1, RelayName = "Ventilation" },
                new DeviceTypeRelayRow { IDDeviceTypeRelay = 2, RelayName = "Light" },
                new DeviceTypeRelayRow { IDDeviceTypeRelay = 3, RelayName = "Heating" },
                new DeviceTypeRelayRow { IDDeviceTypeRelay = 4, RelayName = "Water pump" });
        if (!db.DeviceTypeSensors.Any())
            db.DeviceTypeSensors.AddRange(
                new DeviceTypeSensorRow { IDDeviceTypeSensor = 0, SensorName = "Disabled" },
                new DeviceTypeSensorRow { IDDeviceTypeSensor = 1, SensorName = "dht22" });
        db.SaveChanges();

        _targets[provider] = new Target(provider, conn, deviceType);
    }

    private static int SeedDeviceType(AgrumyDbContext db)
    {
        // IDs 0-3 are reserved by Agrumy.Web's hardcoded switch; pick one outside that range.
        var t = new DeviceTypeRow { IDDeviceType = 999, DeviceTypeName = "greenhouse" };
        db.DeviceTypes.Add(t);
        db.SaveChanges();
        return t.IDDeviceType;
    }

    public AgrumyDbContext NewContext(Target t) => new(DbOptionsFactory.Build(t.Provider, t.ConnectionString));
}

public sealed class RelationalIntegrationTests : IClassFixture<RelationalIntegrationFixture>, IDisposable
{
    private readonly RelationalIntegrationFixture _fx;
    private AgrumyDbContext? _db;
    private EfRepository _repo = null!;

    public RelationalIntegrationTests(RelationalIntegrationFixture fx) => _fx = fx;

    public void Dispose() => _db?.Dispose();

    /// One row per configured engine, or a sentinel that makes every test skip.
    public static IEnumerable<object[]> Providers()
    {
        var rows = new List<object[]>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRUMY_TEST_MYSQL")))
            rows.Add(new object[] { DbProviderKind.MySql });
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRUMY_TEST_POSTGRES")))
            rows.Add(new object[] { DbProviderKind.Postgres });
        return rows.Count > 0 ? rows : new[] { new object[] { (DbProviderKind)255 } };
    }

    // Callable more than once per test to hand it a FRESH context/repo, the same way a new HTTP request gets its own scope.
    private RelationalIntegrationFixture.Target Use(DbProviderKind provider)
    {
        var t = _fx.Targets.FirstOrDefault(x => x.Provider == provider);
        Skip.If(t is null, $"No integration database configured for {provider}.");
        _db?.Dispose();
        _db = new AgrumyDbContext(DbOptionsFactory.Build(t!.Provider, t.ConnectionString));
        // NullCache: these tests verify query/translation correctness against the real engine, not cache behavior.
        _repo = new EfRepository(_db, Options.Create(new AgrumySettings()), NullLogger<EfRepository>.Instance, new NullCache());
        return t;
    }

    private sealed class NullCache : ICache
    {
        public Task<DeviceCache> GetDeviceCacheAsync(string key) => Task.FromResult(new DeviceCache { apiAuth = null });
        public Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class => Task.CompletedTask;
    }

    private static string U() => Guid.NewGuid().ToString("N")[..12];

    private async Task<(int tenantId, int userId, string email)> MakeUser(RelationalIntegrationFixture.Target t, bool enabled = true)
    {
        string tag = U();
        int tenantId = await _repo.TenantAddAsync("T_" + tag);
        var user = new User
        {
            TenantID = tenantId,
            Email = tag + "@ex.com",
            Username = "u_" + tag,
            FirstName = "F",
            LastName = "L",
            Phone = "123",
            DevicePin = "PIN234",
            Enabled = enabled,
        };
        await _repo.UserAddAsync(user, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        var back = await _repo.UserGetAsync(null, user.Email, null);
        Assert.NotNull(back);
        return (tenantId, back.IDUser!.Value, user.Email!);
    }

    private async Task<Device> MakeDevice(RelationalIntegrationFixture.Target t, int tenantId)
    {
        var d = new Device
        {
            TenantID = tenantId,
            DeviceTypeID = t.DeviceTypeId,
            DeviceTypeServiceID = 1,
            ConfigVersion = 1,
            DeviceName = "dev_" + U(),
            MacAddress = U(),
            ApiId = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
            ServicePoint = "api.agrumy.com",
            DeviceSensorEnabled = true,
            DeviceControllerEnabled = true,
        };
        await _repo.DeviceAddAsync(d);
        var saved = await _repo.DeviceGetAsync(tenantId, null, d.ApiId, null);
        Assert.NotNull(saved);
        return saved;
    }

    // Roadmap #303: DbExceptionFilter's duplicate-email/-username messages match on these literal index names - if a future migration ever renames them, this must fail loudly here instead of the filter silently falling back to the generic constraint_violation message.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserAdd_DuplicateEmailOrUsername_StillMatchesTheExpectedConstraintName(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, email) = await MakeUser(t);

        var dupeEmail = new User { TenantID = tenantId, Email = email, Username = "u_" + U(), FirstName = "F", LastName = "L", Phone = "123", DevicePin = "PIN234", Enabled = true };
        Exception exEmail = await Assert.ThrowsAnyAsync<Exception>(
            () => _repo.UserAddAsync(dupeEmail, new UserSecret { PwdHash = "h", PwdSalt = "s" }));
        Assert.True(DbErrorResponse.MentionsConstraint(exEmail, "email_UNIQUE"), "email_UNIQUE no longer matches the real schema's index name.");

        // A failed SaveChangesAsync leaves the poisoned entity tracked - Use() hands back a fresh context/repo, same as a new HTTP request would get.
        Use(provider);
        var (existingTenantId, _, existingEmail) = await MakeUser(t);
        string existingUsername = (await _repo.UserGetAsync(null, existingEmail, null))!.Username!;
        var dupeUsername = new User { TenantID = existingTenantId, Email = "u2_" + U() + "@ex.com", Username = existingUsername, FirstName = "F", LastName = "L", Phone = "123", DevicePin = "PIN234", Enabled = true };
        Exception exUsername = await Assert.ThrowsAnyAsync<Exception>(
            () => _repo.UserAddAsync(dupeUsername, new UserSecret { PwdHash = "h", PwdSalt = "s" }));
        Assert.True(DbErrorResponse.MentionsConstraint(exUsername, "Username_UNIQUE"), "Username_UNIQUE no longer matches the real schema's index name.");
    }

    // Roadmap #293: registration (tenant create + user add + activation token + starting role) is one transaction - a crash/failure partway must never leave a user with no role, or a tenant with no admin.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RegisterUser_NewTenant_CreatesTenantUserTokenAndRole_Atomically(DbProviderKind provider)
    {
        var t = Use(provider);
        string tag = U();
        var user = new User { Email = tag + "@ex.com", Username = "u_" + tag, FirstName = "F", LastName = "L", Phone = "123", Enabled = false };
        var secret = new UserSecret { PwdHash = "h", PwdSalt = "s" };
        string newTenantName = "T_" + tag;

        int idUser = await _repo.RegisterUserAsync(user, secret, existingTenantId: null, newTenantName: newTenantName,
            activationTokenHash: "hash_" + tag, activationTokenExpiresAtUtc: DateTime.UtcNow.AddHours(24),
            startingRoles: new[] { RoleNames.TenantAdmin });

        User? saved = await _repo.UserGetAsync(idUser, null, null);
        Assert.NotNull(saved);
        Assert.True(saved!.TenantID > 0);
        Assert.True(await _repo.TenantGetAsync(newTenantName));
        Assert.Equal(new[] { RoleNames.TenantAdmin }, await _repo.UserRoleNamesGetAsync(idUser));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RegisterUser_FailurePartway_RollsBackTheWholeTransaction(DbProviderKind provider)
    {
        var t = Use(provider);
        string tag = U();
        var (_, _, existingEmail) = await MakeUser(t);

        // Duplicate email - UserAddAsync's own SaveChangesAsync throws partway through the transaction.
        var dupeUser = new User { Email = existingEmail, Username = "u_" + tag, FirstName = "F", LastName = "L", Phone = "123", Enabled = false };
        var secret = new UserSecret { PwdHash = "h", PwdSalt = "s" };
        string newTenantName = "T_" + tag;

        await Assert.ThrowsAnyAsync<Exception>(() => _repo.RegisterUserAsync(dupeUser, secret, existingTenantId: null, newTenantName: newTenantName,
            activationTokenHash: "hash_" + tag, activationTokenExpiresAtUtc: DateTime.UtcNow.AddHours(24),
            startingRoles: new[] { RoleNames.TenantAdmin }));

        // A failed SaveChangesAsync leaves the poisoned entity tracked - Use() hands back a fresh context/repo, same as a new HTTP request would get.
        Use(provider);
        Assert.False(await _repo.TenantGetAsync(newTenantName)); // the tenant the failed registration would have created must not exist either
    }

    // Roadmap #294: deviceCommand otherwise grows unbounded - only terminal (Executed/Expired) rows older than the cutoff are purged, Pending/Acknowledged and recent rows are left alone regardless of status.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task PurgeOldCommands_DeletesOnlyOldTerminalRows(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        DateTime now = DateTime.UtcNow;

        await using (var db = _fx.NewContext(t))
        {
            db.DeviceCommands.AddRange(
                new DeviceCommandRow { DeviceID = d.IDDevice!.Value, ActionType = (int)CommandActionType.Reboot, Status = (int)CommandStatus.Executed, IssuedAt = now.AddDays(-40), ExpiresAt = now.AddDays(-40) }, // old + terminal - purged
                new DeviceCommandRow { DeviceID = d.IDDevice!.Value, ActionType = (int)CommandActionType.ForceOTA, Status = (int)CommandStatus.Expired, IssuedAt = now.AddDays(-40), ExpiresAt = now.AddDays(-40) }, // old + terminal - purged
                new DeviceCommandRow { DeviceID = d.IDDevice!.Value, ActionType = (int)CommandActionType.ForceConfigSync, Status = (int)CommandStatus.Executed, IssuedAt = now.AddDays(-1), ExpiresAt = now.AddDays(-1) }, // terminal but recent - kept
                new DeviceCommandRow { DeviceID = d.IDDevice!.Value, ActionType = (int)CommandActionType.Reboot, Status = (int)CommandStatus.Pending, ExpiresAt = now.AddDays(40), IssuedAt = now.AddDays(-40) }); // old but still active - kept
            await db.SaveChangesAsync();
        }

        await _repo.PurgeOldCommandsAsync(now.AddDays(-30));

        await using var verify = _fx.NewContext(t);
        List<int> remainingStatuses = await verify.DeviceCommands.Where(c => c.DeviceID == d.IDDevice!.Value).Select(c => c.Status).ToListAsync();
        Assert.Equal(2, remainingStatuses.Count);
        Assert.Contains((int)CommandStatus.Executed, remainingStatuses); // the recent one
        Assert.Contains((int)CommandStatus.Pending, remainingStatuses);  // the old-but-active one
    }

    // Roadmap #310: user.TenantID and eventDevice(DeviceID, Date) had no index - every tenant-scoped user list and every device-events/problem-alert scan filtered these columns with a full table scan.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task MissingIndexes_310_NowExist(DbProviderKind provider)
    {
        var t = Use(provider);
        await using var db = _fx.NewContext(t);

        string sql = provider == DbProviderKind.Postgres
            ? "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public'"
            : "SELECT INDEX_NAME AS Value FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()";
        var indexNames = (await db.Database.SqlQueryRaw<string>(sql).ToListAsync())
            .Select(n => n.ToLowerInvariant()).ToHashSet();

        Assert.Contains("ix_user_tenant", indexNames);
        Assert.Contains("ix_eventdevice_device_date", indexNames);
    }

    // Roadmap #302: CURRENT_TIMESTAMP/NOW() column defaults must compute in UTC regardless of the server process's own OS timezone (verified live on invent.hr, whose MySQL @@global.time_zone was SYSTEM/CEST, 2h off UTC) - SessionTimeZoneInterceptor sets this on every connection open.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task NewConnection_SessionTimeZoneIsUtc(DbProviderKind provider)
    {
        var t = Use(provider);
        await using var db = _fx.NewContext(t);

        string sql = provider == DbProviderKind.Postgres
            ? "SELECT current_setting('TIMEZONE') AS \"Value\""
            : "SELECT @@session.time_zone AS Value";
        string tz = await db.Database.SqlQueryRaw<string>(sql).FirstAsync();

        Assert.True(tz is "UTC" or "+00:00", $"Expected the session timezone to be UTC, got '{tz}'.");
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task Schema_HasEveryTable(DbProviderKind provider)
    {
        var t = Use(provider);
        await using var db = _fx.NewContext(t);

        string sql = provider == DbProviderKind.Postgres
            ? "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'"
            : "SELECT TABLE_NAME AS Value FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()";
        var tables = await db.Database.SqlQueryRaw<string>(sql).ToListAsync();

        foreach (var name in new[] { "tenant", "user", "userGroup", "userRole", "userRoleScope",
            "device", "deviceUnit", "deviceUnitZone", "deviceType", "deviceTypeService",
            "deviceTypeRelay", "deviceTypeSensor", "deviceConfigSensor", "deviceConfigController",
            "deviceFirmware", "deviceDiagnostic", "sensorData", "sensorDataReport", "eventDevice",
            "eventService", "serverConfig" })
        {
            Assert.Contains(name, tables);
        }
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task EnsureSchemaAsync_OnProvisionedDb_IsNoOpAndDoesNotThrow(DbProviderKind provider)
    {
        Use(provider);
        await _repo.EnsureSchemaAsync();
        Assert.True(await _repo.TestConnectionAsync());
    }

    // EF sets no charset explicitly; Pomelo applies utf8mb4 implicitly - guards against a silent latin1 regression. MySQL-only.
    [SkippableFact]
    public async Task Fresh_MySql_Schema_Is_Utf8mb4_Not_Latin1()
    {
        var t = Use(DbProviderKind.MySql);
        await using var db = _fx.NewContext(t);

        var tableCharsets = await db.Database.SqlQueryRaw<string>(
            "SELECT SUBSTRING_INDEX(TABLE_COLLATION, '_', 1) AS Value FROM information_schema.TABLES " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'").ToListAsync();
        Assert.NotEmpty(tableCharsets);
        Assert.All(tableCharsets, cs => Assert.Equal("utf8mb4", cs));

        var columnCharsets = await db.Database.SqlQueryRaw<string>(
            "SELECT DISTINCT CHARACTER_SET_NAME AS Value FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND CHARACTER_SET_NAME IS NOT NULL").ToListAsync();
        Assert.Equal(new[] { "utf8mb4" }, columnCharsets);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task Tenant_Add_Get_GetId(DbProviderKind provider)
    {
        Use(provider);
        string name = "T_" + U();
        int id = await _repo.TenantAddAsync(name);

        Assert.True(id > 0);
        Assert.True(await _repo.TenantGetAsync(name));
        Assert.Equal(id, await _repo.TenantGetIdAsync(name));
        Assert.False(await _repo.TenantGetAsync("missing_" + U()));
        Assert.Null(await _repo.TenantGetIdAsync("missing_" + U()));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ServerConfig_AutoCreatesOnce(DbProviderKind provider)
    {
        Use(provider);
        int id = new Random().Next(1000, 9_000_000);
        var a = await _repo.ServerConfigGetAsync(id);
        var b = await _repo.ServerConfigGetAsync(id);

        Assert.Equal(id, a.IDServerConfig);
        Assert.False(string.IsNullOrWhiteSpace(a.ConfigKey));
        Assert.Equal(a.ConfigKey, b.ConfigKey);
        Assert.Equal(80, a.PortHTTP);
        Assert.Equal(443, a.PortHTTPS);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task Tenant_ScheduleTimeZone_UpdateAndGet_RoundTrips(DbProviderKind provider)
    {
        var t = Use(provider);
        int idTenant = await _repo.TenantAddAsync("tz_" + U());

        var tenant = await _repo.TenantGetByIdAsync(idTenant);
        Assert.NotNull(tenant);
        Assert.Null(tenant.ScheduleTimeZone); // not configured yet - per-tenant, not a global fallback

        tenant.ScheduleTimeZone = "Europe/Zagreb";
        await _repo.TenantUpdateAsync(tenant);

        var back = await _repo.TenantGetByIdAsync(idTenant);
        Assert.NotNull(back);
        Assert.Equal("Europe/Zagreb", back.ScheduleTimeZone);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ServerConfig_WeatherFields_UpdateAndGet_RoundTrips(DbProviderKind provider)
    {
        Use(provider);
        int id = new Random().Next(1000, 9_000_000);
        var config = await _repo.ServerConfigGetAsync(id);
        Assert.Null(config.WeatherLocationLat); // not configured yet - inert until an admin sets a location

        config.WeatherLocationLat = 45.815;
        config.WeatherLocationLon = 15.982;
        config.WeatherPollIntervalMinutes = 30;
        config.WeatherRainSkipThreshold = 70.0;
        await _repo.ServerConfigUpdateAsync(config);

        var back = await _repo.ServerConfigGetAsync(id);
        Assert.Equal(45.815, back.WeatherLocationLat);
        Assert.Equal(15.982, back.WeatherLocationLon);
        Assert.Equal(30, back.WeatherPollIntervalMinutes);
        Assert.Equal(70.0, back.WeatherRainSkipThreshold);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ServerConfig_WeatherState_OnlySetByNarrowWriter_NotByAdminUpdate(DbProviderKind provider)
    {
        // ServerConfigUpdateAsync (admin form path) must never touch WeatherRainPredicted/WeatherCheckedAtUtc - only WeatherEvaluator's dedicated writer does.
        Use(provider);
        int id = new Random().Next(1000, 9_000_000);
        var config = await _repo.ServerConfigGetAsync(id);
        Assert.False(config.WeatherRainPredicted);
        Assert.Null(config.WeatherCheckedAtUtc);

        DateTime checkedAt = DateTime.UtcNow;
        await _repo.ServerConfigWeatherStateSetAsync(true, checkedAt, id);

        var afterEvaluator = await _repo.ServerConfigGetAsync(id);
        Assert.True(afterEvaluator.WeatherRainPredicted);
        Assert.NotNull(afterEvaluator.WeatherCheckedAtUtc);

        afterEvaluator.TenantManagementEnabled = true;
        await _repo.ServerConfigUpdateAsync(afterEvaluator);

        var afterAdminSave = await _repo.ServerConfigGetAsync(id);
        Assert.True(afterAdminSave.WeatherRainPredicted);
        Assert.NotNull(afterAdminSave.WeatherCheckedAtUtc);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ServerConfig_SensorDataRetentionDays_UpdateAndGet_RoundTrips(DbProviderKind provider)
    {
        // On Postgres this also exercises ApplyRetentionPolicyAsync; passes even without the TimescaleDB extension installed (graceful fallback).
        Use(provider);
        int id = new Random().Next(1000, 9_000_000);
        var config = await _repo.ServerConfigGetAsync(id);
        Assert.Null(config.SensorDataRetentionDays); // not configured yet - no universal default

        config.SensorDataRetentionDays = 90;
        await _repo.ServerConfigUpdateAsync(config);

        var back = await _repo.ServerConfigGetAsync(id);
        Assert.Equal(90, back.SensorDataRetentionDays);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_AddAndGet_RoundTrips(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string hash = U();
        var expires = DateTime.UtcNow.AddDays(30);

        await _repo.RefreshTokenAddAsync(userId, hash, expires);
        var stored = await _repo.RefreshTokenGetAsync(hash);

        Assert.NotNull(stored);
        Assert.Equal(userId, stored.UserID);
        Assert.Null(stored.RevokedAt);
        Assert.True(Math.Abs((stored.ExpiresAt - expires).TotalSeconds) < 2);
        Assert.Null(await _repo.RefreshTokenGetAsync("no-such-hash-" + U()));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_Rotate_RevokesOldAndActivatesNew(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string oldHash = U();
        string newHash = U();
        await _repo.RefreshTokenAddAsync(userId, oldHash, DateTime.UtcNow.AddDays(30));

        Assert.True(await _repo.RefreshTokenRotateAsync(userId, oldHash, newHash, DateTime.UtcNow.AddDays(30)));

        var old = await _repo.RefreshTokenGetAsync(oldHash);
        var replacement = await _repo.RefreshTokenGetAsync(newHash);
        Assert.NotNull(old);
        Assert.NotNull(old.RevokedAt);
        Assert.NotNull(replacement);
        Assert.Null(replacement.RevokedAt);
        Assert.Equal(userId, replacement.UserID);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_RotateOfAlreadyRevokedToken_IsANoOp(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string hash = U();
        await _repo.RefreshTokenAddAsync(userId, hash, DateTime.UtcNow.AddDays(30));
        await _repo.RefreshTokenRevokeAsync(hash);

        // Simulates a replayed, already-revoked token: rotating it again must not resurrect it.
        Assert.False(await _repo.RefreshTokenRotateAsync(userId, hash, U(), DateTime.UtcNow.AddDays(30)));

        var stillRevoked = await _repo.RefreshTokenGetAsync(hash);
        Assert.NotNull(stillRevoked!.RevokedAt);
    }

    /// Two concurrent rotations of the same token - only one may win; uses two independent DbContext instances since a shared one isn't thread-safe.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_ConcurrentRotateOfSameToken_OnlyOneWins(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string oldHash = U();
        string hashA = U();
        string hashB = U();
        await _repo.RefreshTokenAddAsync(userId, oldHash, DateTime.UtcNow.AddDays(30));

        await using var dbA = _fx.NewContext(t);
        await using var dbB = _fx.NewContext(t);
        var repoA = new EfRepository(dbA, Options.Create(new AgrumySettings()), NullLogger<EfRepository>.Instance, new NullCache());
        var repoB = new EfRepository(dbB, Options.Create(new AgrumySettings()), NullLogger<EfRepository>.Instance, new NullCache());

        bool[] results = await Task.WhenAll(
            repoA.RefreshTokenRotateAsync(userId, oldHash, hashA, DateTime.UtcNow.AddDays(30)),
            repoB.RefreshTokenRotateAsync(userId, oldHash, hashB, DateTime.UtcNow.AddDays(30)));

        Assert.Single(results, true);
        Assert.Single(results, false);

        bool aWon = results[0];
        var winnerToken = await _repo.RefreshTokenGetAsync(aWon ? hashA : hashB);
        var loserToken = await _repo.RefreshTokenGetAsync(aWon ? hashB : hashA);
        Assert.NotNull(winnerToken);
        Assert.Null(winnerToken.RevokedAt);
        Assert.Null(loserToken); // the losing call never inserted a row
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_Revoke_IsIdempotentAndMarksRevoked(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string hash = U();
        await _repo.RefreshTokenAddAsync(userId, hash, DateTime.UtcNow.AddDays(30));

        await _repo.RefreshTokenRevokeAsync(hash);
        await _repo.RefreshTokenRevokeAsync(hash); // second call: still no error
        await _repo.RefreshTokenRevokeAsync("never-issued-" + U()); // unknown token: still no error

        Assert.NotNull((await _repo.RefreshTokenGetAsync(hash))!.RevokedAt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_RevokeAllForUser_RevokesEveryActiveToken(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string hashA = U();
        string hashB = U();
        await _repo.RefreshTokenAddAsync(userId, hashA, DateTime.UtcNow.AddDays(30));
        await _repo.RefreshTokenAddAsync(userId, hashB, DateTime.UtcNow.AddDays(30));

        await _repo.RefreshTokenRevokeAllForUserAsync(userId);

        Assert.NotNull((await _repo.RefreshTokenGetAsync(hashA))!.RevokedAt);
        Assert.NotNull((await _repo.RefreshTokenGetAsync(hashB))!.RevokedAt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task RefreshToken_DuplicateHash_ViolatesUniqueConstraint(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string hash = U();
        await _repo.RefreshTokenAddAsync(userId, hash, DateTime.UtcNow.AddDays(30));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _repo.RefreshTokenAddAsync(userId, hash, DateTime.UtcNow.AddDays(30)));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_Add_Then_Get_By_Every_Key(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, userId, email) = await MakeUser(t);

        var byId = await _repo.UserGetAsync(userId, null, null);
        Assert.NotNull(byId);
        var byEmail = await _repo.UserGetAsync(null, email, null);
        var byName = await _repo.UserGetAsync(null, null, byId.Username);
        Assert.NotNull(byEmail);
        Assert.NotNull(byName);

        Assert.Equal(userId, byEmail.IDUser);
        Assert.Equal(userId, byName.IDUser);
        Assert.Equal(tenantId, byId.TenantID);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_Get_NoMatch_ReturnsNull_NoKey_Throws(DbProviderKind provider)
    {
        Use(provider);
        Assert.Null(await _repo.UserGetAsync(null, "nope_" + U() + "@x.com", null)); // no match -> null
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserGetAsync(null, null, null)); // no key -> throw
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UsersGet_ScopedToTenant(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, userId, _) = await MakeUser(t);
        var list = await _repo.UsersGetAsync(tenantId);

        Assert.Single(list);
        Assert.Equal(userId, list[0].IDUser);
        Assert.Empty(await _repo.UsersGetAsync(-999));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserSecret_Get_And_SetPassword(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, email) = await MakeUser(t);

        var s = await _repo.UserSecretGetAsync(userId, null, null);
        Assert.NotNull(s);
        Assert.Equal("h", s.PwdHash);
        Assert.Equal("s", s.PwdSalt);

        Assert.True(await _repo.UserSetPasswordAsync(email, new UserSecret { PwdHash = "h2", PwdSalt = "s2" }));
        Assert.False(await _repo.UserSetPasswordAsync("missing_" + U() + "@x.com", new UserSecret { PwdHash = "x", PwdSalt = "y" }));

        var s2 = await _repo.UserSecretGetAsync(null, email, null);
        Assert.NotNull(s2);
        Assert.Equal("h2", s2.PwdHash);

        Assert.Null(await _repo.UserSecretGetAsync(null, "missing_" + U() + "@x.com", null));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserSetPasswordAsync_RevokesRefreshTokensAndBumpsTokenCutoff(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, email) = await MakeUser(t);
        string refreshTokenHash = "hash_" + U();
        await _repo.RefreshTokenAddAsync(userId, refreshTokenHash, DateTime.UtcNow.AddDays(30));

        DateTime beforeChange = DateTime.UtcNow;
        await _repo.UserSetPasswordAsync(email, new UserSecret { PwdHash = "h3", PwdSalt = "s3" });

        User? user = await _repo.UserGetAsync(userId, null, null);
        Assert.NotNull(user!.TokensValidAfterUtc);
        Assert.True(user.TokensValidAfterUtc >= beforeChange);
        Assert.NotNull((await _repo.RefreshTokenGetAsync(refreshTokenHash))!.RevokedAt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task BootstrapAdmin_Pending_SetOnce_ThenPermanentlyUnavailable(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, normalUserId, _) = await MakeUser(t);

        string tag = U();
        string setupSecretSalt = AuthenticationProvider.GetSalt();
        await using (var db = _fx.NewContext(t))
        {
            db.Users.Add(new UserRow
            {
                TenantID = 0,
                Email = tag + "_admin@ex.com",
                Username = "boot_" + tag,
                PwdHash = null,
                PwdSalt = null,
                BootstrapSecretHash = AuthenticationProvider.GetHash("correct-setup-secret", setupSecretSalt),
                BootstrapSecretSalt = setupSecretSalt,
                Enabled = true,
                EmailVerified = true,
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await _repo.BootstrapAdminPendingAsync());

        // Wrong setup secret must not be able to claim the account.
        Assert.False(await _repo.BootstrapAdminSetPasswordAsync(new UserSecret { PwdHash = "wrong-h", PwdSalt = "wrong-s" }, "wrong-setup-secret"));
        Assert.True(await _repo.BootstrapAdminPendingAsync());

        Assert.True(await _repo.BootstrapAdminSetPasswordAsync(new UserSecret { PwdHash = "boot-h", PwdSalt = "boot-s" }, "correct-setup-secret"));
        Assert.False(await _repo.BootstrapAdminPendingAsync());

        Assert.False(await _repo.BootstrapAdminSetPasswordAsync(new UserSecret { PwdHash = "again-h", PwdSalt = "again-s" }, "correct-setup-secret"));

        var normalSecret = await _repo.UserSecretGetAsync(normalUserId, null, null);
        Assert.Equal("h", normalSecret!.PwdHash);
    }

    /// UserProfileSetAsync must write ONLY FirstName/LastName/TimeZone, never authorization fields.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserProfileSet_Writes_Only_Profile_Fields(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, userId, email) = await MakeUser(t);

        Assert.True(await _repo.UserProfileSetAsync(email, "NewFirst", "NewLast", "Europe/Zagreb"));
        Assert.False(await _repo.UserProfileSetAsync("missing_" + U() + "@x.com", "X", "Y", null));

        var back = await _repo.UserGetAsync(userId, null, null);
        Assert.NotNull(back);
        Assert.Equal("NewFirst", back.FirstName);
        Assert.Equal("NewLast", back.LastName);
        Assert.Equal("Europe/Zagreb", back.TimeZone);

        Assert.Equal(tenantId, back.TenantID);
        Assert.True(back.Enabled);
        var secret = await _repo.UserSecretGetAsync(userId, null, null);
        Assert.NotNull(secret);
        Assert.Equal("h", secret.PwdHash);

        Assert.True(await _repo.UserProfileSetAsync(email, "NewFirst", "NewLast", null));
        Assert.Null((await _repo.UserGetAsync(userId, null, null))!.TimeZone);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_Update_Changes_Fields(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);

        await _repo.UserUpdateAsync(new User
        {
            IDUser = userId, TenantID = 7, Email = "upd_" + U() + "@x.com", Username = "n_" + U(),
            FirstName = "New", LastName = "Name", Phone = "999", Enabled = false, DevicePin = "HACKED",
        });

        var back = await _repo.UserGetAsync(userId, null, null);
        Assert.NotNull(back);
        Assert.Equal("New", back.FirstName);
        Assert.Equal(7, back.TenantID);
        Assert.False(back.Enabled);
        // UserUpdateAsync must never touch the PIN - its lifecycle belongs solely to UserSetDevicePinAsync.
        Assert.Equal("PIN234", back.DevicePin);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_SetDevicePin_Issues_And_Clears(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);

        DateTime expires = DateTime.UtcNow.AddHours(24);
        Assert.True(await _repo.UserSetDevicePinAsync(userId, "ABC234", expires));
        var issued = await _repo.UserGetAsync(userId, null, null);
        Assert.Equal("ABC234", issued!.DevicePin);
        Assert.NotNull(issued.DevicePinExpires);

        // The PIN is multi-use within its own expiry; null remains a supported explicit-clear operation.
        Assert.True(await _repo.UserSetDevicePinAsync(userId, null, null));
        var cleared = await _repo.UserGetAsync(userId, null, null);
        Assert.Null(cleared!.DevicePin);
        Assert.Null(cleared.DevicePinExpires);

        Assert.False(await _repo.UserSetDevicePinAsync(-1, "ABC234", expires));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_Delete_Guard_And_Delete(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);

        Assert.False(await _repo.UserDeleteAsync(1));
        Assert.False(await _repo.UserDeleteAsync(null));
        Assert.True(await _repo.UserDeleteAsync(userId));
        Assert.Null(await _repo.UserGetAsync(userId, null, null));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task Device_Add_Creates_Two_Config_Rows_And_Links_Them(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        Assert.NotNull(d.DeviceConfigSensorID);
        Assert.NotNull(d.DeviceConfigControllerID);
        Assert.NotNull(await _repo.DeviceConfigSensorGetAsync(d.DeviceConfigSensorID));
        Assert.NotNull(await _repo.DeviceConfigControllerGetAsync(d.DeviceConfigControllerID));
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByDeviceConfigSensorIdAsync(d.DeviceConfigSensorID))!.IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByDeviceConfigControllerIdAsync(d.DeviceConfigControllerID))!.IDDevice);
    }

    // Callers (DeviceRegistration) must not need a follow-up DeviceGetAsync round-trip - the created row's own IDDevice comes back directly.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceAdd_ReturnsCreatedDevice_WithGeneratedId_NoFollowUpGetNeeded(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = new Device
        {
            TenantID = tenantId,
            DeviceTypeID = t.DeviceTypeId,
            DeviceTypeServiceID = 1,
            ConfigVersion = 1,
            DeviceName = "dev_" + U(),
            MacAddress = U(),
            ApiId = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
            ServicePoint = "api.agrumy.com",
        };

        Device created = await _repo.DeviceAddAsync(d);

        Assert.True(created.IDDevice > 0);
        Assert.Equal(d.MacAddress, created.MacAddress);
        Assert.Equal(d.ApiId, created.ApiId);
        Assert.NotNull(created.DeviceConfigSensorID);
        Assert.NotNull(created.DeviceConfigControllerID);
    }

    // DB-level guard against the add/get check-then-act race: the second insert must fail at the DB, not silently create a duplicate row.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceAdd_DuplicateMacAddress_SameTenant_ThrowsConstraintViolation(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d1 = await MakeDevice(t, tenantId);

        var d2 = new Device
        {
            TenantID = tenantId,
            DeviceTypeID = t.DeviceTypeId,
            DeviceTypeServiceID = 1,
            ConfigVersion = 1,
            DeviceName = "dev_" + U(),
            MacAddress = d1.MacAddress,
            ApiId = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
            ServicePoint = "api.agrumy.com",
        };

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _repo.DeviceAddAsync(d2));
        Assert.Equal(DbFailureKind.ConstraintViolation, _repo.ClassifyException(ex));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceAdd_SameMacAddress_DifferentTenant_Succeeds(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenant1, _, _) = await MakeUser(t);
        var (tenant2, _, _) = await MakeUser(t);
        var d1 = await MakeDevice(t, tenant1);

        var d2 = new Device
        {
            TenantID = tenant2,
            DeviceTypeID = t.DeviceTypeId,
            DeviceTypeServiceID = 1,
            ConfigVersion = 1,
            DeviceName = "dev_" + U(),
            MacAddress = d1.MacAddress,
            ApiId = Guid.NewGuid().ToString(),
            ApiKey = Guid.NewGuid().ToString(),
            ServicePoint = "api.agrumy.com",
        };

        await _repo.DeviceAddAsync(d2);
        var back = await _repo.DeviceGetAsync(tenant2, null, null, d1.MacAddress);
        Assert.NotNull(back);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceGet_Lookups_Are_Tenant_Scoped(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, d.IDDevice, null, null))!.IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, null, d.ApiId, null))!.IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, null, null, d.MacAddress))!.IDDevice);
        Assert.Null(await _repo.DeviceGetAsync(tenantId + 12345, null, d.ApiId, null));
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByIdAsync(d.IDDevice))!.IDDevice);
        // DeviceGetByApiIdAsync has no tenant filter - device-comm endpoints have no tenant context.
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByApiIdAsync(d.ApiId))!.IDDevice);
        Assert.Null(await _repo.DeviceGetByApiIdAsync("no-such-api-id-" + U()));
        Assert.Single(await _repo.DevicesGetAsync(tenantId));
        Assert.True(await _repo.DeviceCheckMacAddressAsync(tenantId, d.MacAddress));
        Assert.False(await _repo.DeviceCheckMacAddressAsync(tenantId, "no_" + U()));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUpdate_BumpsRowsOwnConfigVersion_IgnoringStalePayloadValue(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId); // ConfigVersion == 1

        d.DeviceName = "renamed";
        // A stale/forged payload value - two concurrent edits could both submit this same number, so
        // it must never be the base DeviceUpdateAsync increments from, only the freshly-read row's own.
        d.ConfigVersion = 999;
        await _repo.DeviceUpdateAsync(d);

        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.NotNull(back);
        Assert.Equal("renamed", back.DeviceName);
        Assert.Equal(2, back.ConfigVersion);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceConfig_Updates_Persist_And_Bump_Device_ConfigVersion(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        int v0 = (await _repo.DeviceGetByIdAsync(d.IDDevice))!.ConfigVersion!.Value;

        await _repo.DeviceConfigSensorUpdateAsync(d.IDDevice, new DeviceConfigSensor
        {
            IDDeviceConfigSensor = d.DeviceConfigSensorID, SensorTemp = 1, SensorHumid = 1, SensorCo2 = 1,
        });
        Assert.Equal(v0 + 1, (await _repo.DeviceGetByIdAsync(d.IDDevice))!.ConfigVersion);

        await _repo.DeviceConfigControllerUpdateAsync(d.IDDevice, new DeviceConfigController
        {
            IDDeviceConfigController = d.DeviceConfigControllerID, RelayEnabled = true,
            Relays = [new DeviceRelaySlot { Slot = 1, RelayFunction = 2 }],
        });
        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.NotNull(back);
        Assert.Equal(v0 + 2, back.ConfigVersion);

        var ctrl = await _repo.DeviceConfigControllerGetAsync(d.DeviceConfigControllerID);
        Assert.True(ctrl!.RelayEnabled);
        Assert.Equal(2, Assert.Single(ctrl.Relays).RelayFunction);
        Assert.Equal(1, (await _repo.DeviceConfigSensorGetAsync(d.DeviceConfigSensorID))!.SensorTemp);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceFleetGet_ControllerCapable_TrueFromEitherDeviceTypeOrKnownKit(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);

        var basicUnknownKit = await MakeDevice(t, tenantId);
        var recognizedKit = await MakeDevice(t, tenantId);

        // Flip DeviceControllerEnabled off (MakeDevice defaults it true) so capability can ONLY come from the DeviceType/Kit signal, isolating each half of the OR.
        basicUnknownKit.DeviceControllerEnabled = false;
        await _repo.DeviceUpdateAsync(basicUnknownKit);
        await _repo.DeviceDiagnosticUpsertAsync(basicUnknownKit.IDDevice!.Value, tenantId,
            new DeviceConfigPoll { ConfigVersion = 1, Kit = "" });

        recognizedKit.DeviceControllerEnabled = false;
        await _repo.DeviceUpdateAsync(recognizedKit);
        await _repo.DeviceDiagnosticUpsertAsync(recognizedKit.IDDevice!.Value, tenantId,
            new DeviceConfigPoll { ConfigVersion = 1, Kit = "KC868-A6" });

        var fleet = await _repo.DeviceFleetGetAsync(tenantId);
        Assert.False(fleet.Single(f => f.IDDevice == basicUnknownKit.IDDevice).ControllerCapable);
        Assert.True(fleet.Single(f => f.IDDevice == recognizedKit.IDDevice).ControllerCapable);
    }

    // DeviceConfig*UpdateAsync must resolve the row to write from idDevice's OWN config-id column, never from the DeviceConfig*.ID* field on the posted payload.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceConfigUpdate_IgnoresTamperedConfigId_NeverWritesAnotherDevicesRow(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var a = await MakeDevice(t, tenantId);
        var b = await MakeDevice(t, tenantId);

        await _repo.DeviceConfigSensorUpdateAsync(a.IDDevice, new DeviceConfigSensor
        {
            IDDeviceConfigSensor = b.DeviceConfigSensorID, // tampered: points at B's row
            SensorTemp = 7,
        });
        Assert.Equal(7, (await _repo.DeviceConfigSensorGetAsync(a.DeviceConfigSensorID))!.SensorTemp);
        Assert.NotEqual(7, (await _repo.DeviceConfigSensorGetAsync(b.DeviceConfigSensorID))!.SensorTemp);

        await _repo.DeviceConfigControllerUpdateAsync(a.IDDevice, new DeviceConfigController
        {
            IDDeviceConfigController = b.DeviceConfigControllerID, // tampered: points at B's row
            Relays = [new DeviceRelaySlot { Slot = 1, RelayFunction = 3 }],
        });
        Assert.Equal(3, Assert.Single((await _repo.DeviceConfigControllerGetAsync(a.DeviceConfigControllerID))!.Relays).RelayFunction);
        Assert.Empty((await _repo.DeviceConfigControllerGetAsync(b.DeviceConfigControllerID))!.Relays);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZone_WaterPumpSafetyLimits_SeededOnCreate_ThenOverridable(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);

        Assert.NotNull(zone.WaterPumpMaxRunSeconds);
        Assert.NotNull(zone.WaterPumpCooldownSeconds);

        zone.WaterPumpMaxRunSeconds = 900;
        zone.WaterPumpCooldownSeconds = 120;
        await _repo.DeviceUnitZoneUpdateAsync(zone);

        var overridden = await _repo.DeviceUnitZoneGetByIdAsync(zone.IDDeviceUnitZone);
        Assert.Equal(900, overridden!.WaterPumpMaxRunSeconds);
        Assert.Equal(120, overridden.WaterPumpCooldownSeconds);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZone_SkipWaterPumpWhenRainPredicted_DefaultsFalse_ThenOverridable(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);

        Assert.False(zone.SkipWaterPumpWhenRainPredicted);

        zone.SkipWaterPumpWhenRainPredicted = true;
        await _repo.DeviceUnitZoneUpdateAsync(zone);

        var updated = await _repo.DeviceUnitZoneGetByIdAsync(zone.IDDeviceUnitZone);
        Assert.True(updated!.SkipWaterPumpWhenRainPredicted);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneRule_AddAndDelete_AreIndependent_NotWholeListReplace(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);

        int rule1 = await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.Ventilation,
            ConditionType = ConditionType.Schedule,
            ConditionConfig = JsonSerializer.SerializeToNode(new ScheduleConditionConfig(0b0111110, 21600, 1800), ConditionConfigJson.Options),
        });
        int rule2 = await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.Ventilation,
            ConditionType = ConditionType.Schedule,
            ConditionConfig = JsonSerializer.SerializeToNode(new ScheduleConditionConfig(0b0111110, 50400, 900), ConditionConfigJson.Options),
        });
        Assert.Equal(2, (await _repo.DeviceUnitZoneRulesGetAsync(zone.IDDeviceUnitZone!.Value)).Count);

        await _repo.DeviceUnitZoneRuleDeleteAsync(rule1);

        var remaining = Assert.Single(await _repo.DeviceUnitZoneRulesGetAsync(zone.IDDeviceUnitZone!.Value));
        Assert.Equal(rule2, remaining.IDDeviceUnitZoneRule);
        var config = remaining.ConditionConfig.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options);
        Assert.Equal(50400, config!.Start);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneRule_MultipleRulesSameFunction_BothPersist(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);

        await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.WaterPump,
            ConditionType = ConditionType.Threshold,
            ConditionConfig = JsonSerializer.SerializeToNode(new ThresholdConditionConfig(10, 5), ConditionConfigJson.Options),
        });
        await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.WaterPump,
            ConditionType = ConditionType.Interval,
            ConditionConfig = JsonSerializer.SerializeToNode(new IntervalConditionConfig(3600, 300), ConditionConfigJson.Options),
        });

        var rules = await _repo.DeviceUnitZoneRulesGetAsync(zone.IDDeviceUnitZone!.Value);
        Assert.Equal(2, rules.Count);
        Assert.All(rules, r => Assert.Equal(RelayFunction.WaterPump, r.RelayFunction));
        Assert.Contains(rules, r => r.ConditionType == ConditionType.Threshold);
        Assert.Contains(rules, r => r.ConditionType == ConditionType.Interval);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneRule_AssignedDevice_ReadsZonesRulesAndSafetyLimits(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.Light,
            ConditionType = ConditionType.Threshold,
            ConditionConfig = JsonSerializer.SerializeToNode(new ThresholdConditionConfig(200, 20), ConditionConfigJson.Options),
        });

        var rules = await _repo.DeviceUnitZoneRulesGetAsync(zone.IDDeviceUnitZone!.Value);
        var rule = Assert.Single(rules);
        Assert.Equal(RelayFunction.Light, rule.RelayFunction);

        var zoneAfter = await _repo.DeviceUnitZoneGetByIdAsync(zone.IDDeviceUnitZone);
        Assert.Equal(zone.WaterPumpMaxRunSeconds, zoneAfter!.WaterPumpMaxRunSeconds);

        var deviceAfter = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.Equal(zone.IDDeviceUnitZone, deviceAfter!.DeviceUnitZoneID);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneDelete_AlsoDeletesItsRules(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);
        int ruleId = await _repo.DeviceUnitZoneRuleAddAsync(new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = zone.IDDeviceUnitZone!.Value,
            RelayFunction = RelayFunction.Heating,
            ConditionType = ConditionType.Threshold,
            ConditionConfig = JsonSerializer.SerializeToNode(new ThresholdConditionConfig(18, 1), ConditionConfigJson.Options),
        });

        await _repo.DeviceUnitZoneDeleteAsync(zone.IDDeviceUnitZone!.Value);

        Assert.Null(await _repo.DeviceUnitZoneRuleGetByIdAsync(ruleId));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceDelete_Removes_Device_And_Its_Config_Rows(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // FK from diagnostic to device is NoAction, not Cascade - a diagnostic row must not block delete.
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice!.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });

        await _repo.DeviceDeleteAsync(d.IDDevice, tenantId);

        Assert.Null(await _repo.DeviceGetByIdAsync(d.IDDevice));
        await using var db = _fx.NewContext(t);
        Assert.False(await db.DeviceConfigSensors.AnyAsync(c => c.IDDeviceConfigSensor == d.DeviceConfigSensorID));
        Assert.False(await db.DeviceConfigControllers.AnyAsync(c => c.IDDeviceConfigController == d.DeviceConfigControllerID));
        Assert.False(await db.DeviceDiagnostics.AnyAsync(x => x.DeviceID == d.IDDevice));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceDiagnostic_Upsert_Records_Heartbeat_And_Fleet_Reports_It(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        var row = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.False(row.Online);
        Assert.Null(row.LastSeenAt);

        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice!.Value, tenantId, new DeviceConfigPoll
        {
            ConfigVersion = 1, Uptime = 3600, Rssi = -67, FreeHeap = 153212, FirmwareVersion = "0.1.2",
        });

        row = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.True(row.Online);
        Assert.NotNull(row.LastSeenAt);
        Assert.Equal(3600, row.UptimeSeconds);
        Assert.Equal(-67, row.RssiDbm);
        Assert.Equal(153212, row.FreeHeapBytes);
        Assert.Equal("0.1.2", row.FirmwareVersion);

        DateTime firstSeen = row.LastSeenAt!.Value;
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        row = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.True(row.LastSeenAt >= firstSeen);
        Assert.Equal("0.1.2", row.FirmwareVersion);
        Assert.Equal(-67, row.RssiDbm);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceFleet_Is_Tenant_Scoped_And_Null_Means_All(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        Assert.DoesNotContain(await _repo.DeviceFleetGetAsync(tenantId + 12345), f => f.IDDevice == d.IDDevice);
        Assert.Contains(await _repo.DeviceFleetGetAsync(null), f => f.IDDevice == d.IDDevice);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task OfflineAlertCandidates_Reflect_Diagnostics_And_NotifiedAt_RoundTrips(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        // MakeDevice leaves Enabled null; only Enabled == true counts as a candidate (null reads as disabled, same convention Fleet.cshtml uses).
        d.Enabled = true;
        await _repo.DeviceUpdateAsync(d);

        var candidate = Assert.Single(await _repo.OfflineAlertCandidatesGetAsync(), c => c.IDDevice == d.IDDevice);
        Assert.Equal(tenantId, candidate.TenantID);
        Assert.Null(candidate.LastSeenAt);
        Assert.Null(candidate.OfflineNotifiedAt);

        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice!.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        candidate = Assert.Single(await _repo.OfflineAlertCandidatesGetAsync(), c => c.IDDevice == d.IDDevice);
        Assert.NotNull(candidate.LastSeenAt);
        Assert.Null(candidate.OfflineNotifiedAt);

        DateTime notifiedAt = DateTime.UtcNow;
        await _repo.DeviceOfflineNotifiedSetAsync(d.IDDevice.Value, notifiedAt);
        candidate = Assert.Single(await _repo.OfflineAlertCandidatesGetAsync(), c => c.IDDevice == d.IDDevice);
        Assert.NotNull(candidate.OfflineNotifiedAt);

        await _repo.DeviceOfflineNotifiedSetAsync(d.IDDevice.Value, null);
        candidate = Assert.Single(await _repo.OfflineAlertCandidatesGetAsync(), c => c.IDDevice == d.IDDevice);
        Assert.Null(candidate.OfflineNotifiedAt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceFirmwareLatest_Picks_Newest_By_DateAdded(DbProviderKind provider)
    {
        var t = Use(provider);
        int type = new Random().Next(5000, 9_000_000);
        await using (var db = _fx.NewContext(t))
        {
            db.DeviceFirmwares.Add(new DeviceFirmwareRow { DeviceTypeID = type, Version = "0.1.0", Url = "u1", DateAdded = DateTime.Now.AddDays(-2) });
            db.DeviceFirmwares.Add(new DeviceFirmwareRow { DeviceTypeID = type, Version = "0.2.0", Url = "u2", DateAdded = DateTime.Now });
            await db.SaveChangesAsync();
        }

        Assert.Equal("0.2.0", (await _repo.DeviceFirmwareLatestGetAsync(type))!.Version);
        Assert.Null(await _repo.DeviceFirmwareLatestGetAsync(-1));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task FirmwareCatalog_Add_ListForBoard_FiltersBySource_And_DeleteBySource_KeepsLegacyRows(DbProviderKind provider)
    {
        var t = Use(provider);
        string board = "board" + U().ToLowerInvariant();

        int gh = await _repo.FirmwareAddAsync(new DeviceFirmware { Board = board, Version = "1.0.0", Source = FirmwareSource.GitHub, Url = "gh", FileName = $"agrumy-{board}-v1.0.0.bin", Sha256 = new string('a', 64), SizeBytes = 123 });
        await _repo.FirmwareAddAsync(new DeviceFirmware { Board = board, Version = "1.1.0", Source = FirmwareSource.Local, Url = "local" });
        await _repo.FirmwareAddAsync(new DeviceFirmware { Board = board, Version = "2.0.0", Source = FirmwareSource.Custom, Url = "custom" });
        await using (var db = _fx.NewContext(t))
        {
            db.DeviceFirmwares.Add(new DeviceFirmwareRow { DeviceTypeID = 424242, Version = "0.0.1", Url = "legacy", Source = (int)FirmwareSource.GitHub }); // pre-#94 row: no Board
            await db.SaveChangesAsync();
        }

        var visible = await _repo.FirmwareListForBoardAsync(board, [FirmwareSource.GitHub, FirmwareSource.Local]);
        Assert.Equal(["1.0.0", "1.1.0"], visible.Select(v => v.Version).OrderBy(v => v));

        DeviceFirmware? saved = await _repo.FirmwareGetAsync(gh);
        Assert.Equal((board, new string('a', 64), 123L, FirmwareSource.GitHub), (saved!.Board, saved.Sha256, saved.SizeBytes, saved.Source));

        Assert.True(await _repo.FirmwareDeleteBySourceAsync(FirmwareSource.GitHub) >= 1);
        Assert.DoesNotContain(await _repo.FirmwareListForBoardAsync(board, [FirmwareSource.GitHub]), f => f.Board == board);
        Assert.NotNull(await _repo.DeviceFirmwareLatestGetAsync(424242)); // legacy row survived the sweep
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task Fleet_Reports_Board_LatestVersion_And_UpdateAvailable_By_Semver(DbProviderKind provider)
    {
        var t = Use(provider);
        (int tenantId, _, _) = await MakeUser(t);
        Device d = await MakeDevice(t, tenantId);
        string board = "board" + U().ToLowerInvariant();
        await _repo.FirmwareAddAsync(new DeviceFirmware { Board = board, Version = "1.9.0", Source = FirmwareSource.GitHub, Url = "a" });
        await _repo.FirmwareAddAsync(new DeviceFirmware { Board = board, Version = "1.10.0", Source = FirmwareSource.Local, Url = "b" }); // Local always visible; semver-newest

        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice!.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1, FirmwareVersion = "1.9.0", Board = board });
        Assert.Equal(board, await _repo.DeviceBoardGetAsync(d.IDDevice.Value));

        var row = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.Equal((board, "1.10.0", true, false), (row.Board, row.LatestFirmwareVersion, row.FirmwareUpdateAvailable, row.FirmwareUpdatePending));

        await _repo.DeviceFirmwareUpdateSetAsync(d.IDDevice.Value, true, "1.9.0");
        row = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.Equal((true, "1.9.0"), (row.FirmwareUpdatePending, row.FirmwareTargetVersion));
        Assert.Equal("1.9.0", (await _repo.DeviceGetByIdAsync(d.IDDevice))!.FirmwareTargetVersion);

        await _repo.DeviceFirmwareUpdateSetAsync(d.IDDevice.Value, false, null);
        Device back = (await _repo.DeviceGetByIdAsync(d.IDDevice))!;
        Assert.Equal((false, (string?)null), (back.FirmwareUpdate, back.FirmwareTargetVersion));

        // A heartbeat without a Board field must not erase one already recorded.
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        Assert.Equal(board, await _repo.DeviceBoardGetAsync(d.IDDevice.Value));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ServerConfig_FirmwareSource_RoundTrips_And_Defaults_To_GitHub_Repository(DbProviderKind provider)
    {
        Use(provider);
        int id = new Random().Next(200_000, 900_000);
        ServerConfig fresh = await _repo.ServerConfigGetAsync(id);
        Assert.Equal(FirmwareSource.GitHub, fresh.FirmwareSource);
        Assert.Equal("dopiskur/AgrumyFirmware", fresh.FirmwareGitHubRepository);

        fresh.FirmwareSource = FirmwareSource.Custom;
        fresh.FirmwareGitHubRepository = "someone/fork";
        fresh.FirmwareCustomRepositoryUrl = "https://fw.example.com/manifest.json";
        await _repo.ServerConfigUpdateAsync(fresh);

        ServerConfig back = await _repo.ServerConfigGetAsync(id);
        Assert.Equal((FirmwareSource.Custom, "someone/fork", "https://fw.example.com/manifest.json"), (back.FirmwareSource, back.FirmwareGitHubRepository, back.FirmwareCustomRepositoryUrl));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceType_Lists_Return_Seeded_Rows(DbProviderKind provider)
    {
        var t = Use(provider);
        Assert.Contains(await _repo.DeviceTypeGetAsync(), x => x.IDDeviceType == t.DeviceTypeId);
        Assert.Contains(await _repo.DeviceTypeServiceGetAsync(), x => x.IDDeviceTypeService == 1);
        Assert.Contains(await _repo.DeviceTypeRelayGetAsync(), x => x.IDDeviceTypeRelay == 1);
        Assert.Contains(await _repo.DeviceTypeSensorGetAsync(), x => x.IDDeviceTypeSensor == 1);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataPush_Parses_String_Measurements_And_Fills_Missing_Date(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        var payload = new JsonArray(
            new JsonObject
            {
                ["deviceID"] = d.IDDevice, ["tenantID"] = tenantId, ["deviceUnitID"] = 0, ["deviceUnitZoneID"] = 0,
                ["temperature"] = "26.13", ["humidity"] = "47.5", ["co2"] = "408", ["battery"] = null,
                ["dateCreated"] = "2026-08-29 09:50:00",
            },
            new JsonObject
            {
                ["deviceID"] = d.IDDevice, ["tenantID"] = tenantId, ["deviceUnitID"] = 0, ["deviceUnitZoneID"] = 0,
                ["temperature"] = "27.0", ["co2"] = "410",
            });

        await _repo.SensorDataPushAsync(payload, d.IDDevice!.Value, tenantId, 0, 0);

        await using var db = _fx.NewContext(t);
        var rows = await db.SensorData.Where(r => r.DeviceID == d.IDDevice).OrderBy(r => r.IDSensorData).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(26.13, rows[0].Temperature);
        Assert.Equal(new DateTime(2026, 8, 29, 9, 50, 0), rows[0].DateCreated);
        Assert.NotNull(rows[1].DateCreated);
        // UtcNow, matching the push endpoint's UTC fallback - local Now would be ahead of it.
        Assert.True(rows[1].DateCreated > DateTime.UtcNow.AddMinutes(-5));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataPush_Uses_Caller_Identity_And_Ignores_Payload_Ids(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        var payload = new JsonArray(
            new JsonObject
            {
                ["deviceID"] = d.IDDevice!.Value + 999_999,
                ["tenantID"] = tenantId + 999_999,
                ["deviceUnitID"] = 7,
                ["deviceUnitZoneID"] = 9,
                ["temperature"] = "21.5",
            });

        await _repo.SensorDataPushAsync(payload, d.IDDevice!.Value, tenantId, 0, 0);

        await using var db = _fx.NewContext(t);
        var row = await db.SensorData.SingleAsync(r => r.DeviceID == d.IDDevice!.Value);
        Assert.Equal(tenantId, row.TenantID);
        Assert.Equal(0, row.DeviceUnitID);
        Assert.Equal(0, row.DeviceUnitZoneID);
        Assert.Equal(21.5, row.Temperature);
        Assert.False(await db.SensorData.AnyAsync(r => r.DeviceID == d.IDDevice!.Value + 999_999));
        Assert.False(await db.SensorData.AnyAsync(r => r.TenantID == tenantId + 999_999));
    }

    private async Task<(DeviceUnit Unit, DeviceUnitZone Zone)> MakeUnitAndZone(int? tenantId)
    {
        var unit = await _repo.DeviceUnitAddAsync(new DeviceUnit { TenantID = tenantId, DeviceUnitName = "Unit_" + U() });
        var zone = await _repo.DeviceUnitZoneAddAsync(new DeviceUnitZone
        {
            TenantID = tenantId,
            DeviceUnitID = unit.IDDeviceUnit!.Value,
            DeviceUnitZoneName = "Zone_" + U(),
        });
        return (unit, zone);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnit_Contains_MultipleZones(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone1) = await MakeUnitAndZone(tenantId);
        var zone2 = await _repo.DeviceUnitZoneAddAsync(new DeviceUnitZone
        {
            TenantID = tenantId,
            DeviceUnitID = unit.IDDeviceUnit!.Value,
            DeviceUnitZoneName = "Zone_" + U(),
        });

        var zones = await _repo.DeviceUnitZonesGetAsync(unit.IDDeviceUnit!.Value);
        Assert.Equal(2, zones.Count);
        Assert.Contains(zones, z => z.IDDeviceUnitZone == zone1.IDDeviceUnitZone);
        Assert.Contains(zones, z => z.IDDeviceUnitZone == zone2.IDDeviceUnitZone);
        Assert.All(zones, z => Assert.Equal(unit.IDDeviceUnit, z.DeviceUnitID));
    }

    // Only the shared IDDeviceUnit=0 sentinel is global; everything else must stay tenant-scoped.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitsGet_IsTenantScoped(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenant1, _, _) = await MakeUser(t);
        var (tenant2, _, _) = await MakeUser(t);
        var (unit1, _) = await MakeUnitAndZone(tenant1);
        var (unit2, _) = await MakeUnitAndZone(tenant2);

        var seenByTenant1 = await _repo.DeviceUnitsGetAsync(tenant1);
        Assert.Contains(seenByTenant1, u => u.IDDeviceUnit == unit1.IDDeviceUnit);
        Assert.DoesNotContain(seenByTenant1, u => u.IDDeviceUnit == unit2.IDDeviceUnit);
        Assert.DoesNotContain(seenByTenant1, u => u.IDDeviceUnit == 0); // sentinel never listed as a real unit
    }

    // Unassigning resets both FKs to NULL without bumping ConfigVersion - pure bookkeeping, no device config change.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceAssignToZone_SetsUnitAndZone_AndBumpsConfigVersion_UnassignDoesNot(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeDevice(t, tenantId);
        int originalConfigVersion = d.ConfigVersion!.Value;

        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        var assigned = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.Equal(unit.IDDeviceUnit, assigned!.DeviceUnitID);
        Assert.Equal(zone.IDDeviceUnitZone, assigned.DeviceUnitZoneID);
        Assert.Equal(originalConfigVersion + 1, assigned.ConfigVersion);

        await _repo.DeviceUnassignFromZoneAsync(d.IDDevice.Value);

        var unassigned = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.Null(unassigned!.DeviceUnitID);
        Assert.Null(unassigned.DeviceUnitZoneID);
        Assert.Equal(originalConfigVersion + 1, unassigned.ConfigVersion); // unchanged by the unassign
    }

    // Roadmap #313: a never-assigned device must read back NULL, not the old 0 sentinel, and the Fleet page's "unassigned first" sort must still work against NULL.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task NewDevice_IsUnassigned_WithNullNotZero_AndSortsFirstOnFleet(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var unassigned = await MakeDevice(t, tenantId);
        var (_, zone) = await MakeUnitAndZone(tenantId);
        var assigned = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(assigned.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        var fetched = await _repo.DeviceGetByIdAsync(unassigned.IDDevice);
        Assert.Null(fetched!.DeviceUnitID);
        Assert.Null(fetched.DeviceUnitZoneID);
        Assert.Contains(await _repo.DeviceUnassignedGetAsync(tenantId, controllerCapable: true), d => d.IDDevice == unassigned.IDDevice);

        var fleet = (await _repo.DeviceFleetGetAsync(tenantId)).ToList();
        Assert.Null(fleet.Single(f => f.IDDevice == unassigned.IDDevice).DeviceUnitID);
        int unassignedIndex = fleet.FindIndex(f => f.IDDevice == unassigned.IDDevice);
        int assignedIndex = fleet.FindIndex(f => f.IDDevice == assigned.IDDevice);
        Assert.True(unassignedIndex < assignedIndex);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnassignedGet_ExcludesAlreadyAssigned_FiltersByCapability(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);
        var assigned = await MakeDevice(t, tenantId); // sensor+controller capable, per MakeDevice
        var unassigned = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(assigned.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        var controllerCandidates = await _repo.DeviceUnassignedGetAsync(tenantId, controllerCapable: true);
        Assert.Contains(controllerCandidates, x => x.IDDevice == unassigned.IDDevice);
        Assert.DoesNotContain(controllerCandidates, x => x.IDDevice == assigned.IDDevice);

        var sensorCandidates = await _repo.DeviceUnassignedGetAsync(tenantId, controllerCapable: false);
        Assert.Contains(sensorCandidates, x => x.IDDevice == unassigned.IDDevice);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneHasController_TrueOnlyAfterAControllerCapableDeviceIsAssigned(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (_, zone) = await MakeUnitAndZone(tenantId);
        var controller = await MakeDevice(t, tenantId);

        Assert.False(await _repo.DeviceUnitZoneHasControllerAsync(zone.IDDeviceUnitZone!.Value));
        await _repo.DeviceAssignToZoneAsync(controller.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        Assert.True(await _repo.DeviceUnitZoneHasControllerAsync(zone.IDDeviceUnitZone!.Value));
    }

    // Averages the LATEST reading per device per sensor type; unreported types are omitted (null, not zero).
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_AveragesLatestReadingPerDevice_PerSensorType(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d1 = await MakeDevice(t, tenantId);
        var d2 = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d1.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceAssignToZoneAsync(d2.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        // d1: an older then a newer reading - only the newer one (20.0) must count.
        await _repo.SensorDataPushAsync(new JsonArray(
            new JsonObject { ["temperature"] = "10.0", ["dateCreated"] = "2026-08-01 00:00:00" }),
            d1.IDDevice!.Value, tenantId, unit.IDDeviceUnit, zone.IDDeviceUnitZone);
        await _repo.SensorDataPushAsync(new JsonArray(
            new JsonObject { ["temperature"] = "20.0", ["dateCreated"] = "2026-08-02 00:00:00" }),
            d1.IDDevice!.Value, tenantId, unit.IDDeviceUnit, zone.IDDeviceUnitZone);
        // d2: reports humidity only - never sent a temperature, must not drag the temperature average down.
        await _repo.SensorDataPushAsync(new JsonArray(
            new JsonObject { ["humidity"] = "50.0", ["dateCreated"] = "2026-08-02 00:00:00" }),
            d2.IDDevice!.Value, tenantId, unit.IDDeviceUnit, zone.IDDeviceUnitZone);

        var unitDashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(2, unitDashboard.DeviceCount);
        Assert.Equal(1, unitDashboard.ZoneCount);
        Assert.Equal(20.0, unitDashboard.Averages.Temperature); // only d1's latest reading, d2 never reported temperature
        Assert.Equal(50.0, unitDashboard.Averages.Humidity);    // only d2 reported humidity

        var zoneDetail = await _repo.DeviceUnitZoneDashboardGetAsync(zone.IDDeviceUnitZone!.Value);
        Assert.NotNull(zoneDetail);
        Assert.Equal(2, zoneDetail!.Devices.Count);
        Assert.Equal(20.0, zoneDetail.Averages.Temperature);
    }

    private async Task<Device> MakeEnabledDevice(RelationalIntegrationFixture.Target t, int tenantId)
    {
        var d = new Device
        {
            TenantID = tenantId, DeviceTypeID = t.DeviceTypeId, DeviceTypeServiceID = 1, ConfigVersion = 1,
            DeviceName = "dev_" + U(), MacAddress = U(), ApiId = Guid.NewGuid().ToString(), ApiKey = Guid.NewGuid().ToString(),
            ServicePoint = "api.agrumy.com", DeviceSensorEnabled = true, DeviceControllerEnabled = true, Enabled = true,
        };
        await _repo.DeviceAddAsync(d);
        var saved = await _repo.DeviceGetAsync(tenantId, null, d.ApiId, null);
        Assert.NotNull(saved);
        return saved;
    }

    // A device that has never polled (LastSeenAt null) counts as offline, same as Fleet's ComputeOnline.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_RedWhenEnabledDeviceNeverSeen(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Red, dashboard.Status);
        var zoneDashboard = Assert.Single(await _repo.DeviceUnitZoneDashboardListGetAsync(unit.IDDeviceUnit!.Value));
        Assert.Equal(ZoneStatus.Red, zoneDashboard.Status);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_GreenWhenOnlineAndNoProblems(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Green, dashboard.Status);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_OrangeWhenOnlineButRecentProblemEvent(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.AuthFailed, "test");

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Orange, dashboard.Status);
        var alert = Assert.Single(dashboard.ProblemAlerts);
        Assert.Equal("AuthFailed", alert.EventType);
        Assert.Equal(d.IDDevice, alert.DeviceID);
    }

    // Acknowledging the only problem event must clear Orange immediately, without waiting for the expiry window.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_GreenAfterAcknowledgingOnlyProblemEvent(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.AuthFailed, "test");

        int idEventDevice = Assert.Single(await _repo.EventDeviceGetAsync(d.IDDevice, tenantId)).IDEventDevice!.Value;
        Assert.True(await _repo.EventDeviceAcknowledgeAsync(idEventDevice, tenantId));

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Green, dashboard.Status);
        Assert.Empty(dashboard.ProblemAlerts);
    }

    // A foreign tenant's event id must match zero rows - same ownership-lens rule as every other Device sub-resource write.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_AcknowledgeWrongTenant_IsNoOp(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.AuthFailed, "test");
        int idEventDevice = Assert.Single(await _repo.EventDeviceGetAsync(d.IDDevice, tenantId)).IDEventDevice!.Value;

        Assert.False(await _repo.EventDeviceAcknowledgeAsync(idEventDevice, tenantId + 999));

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Orange, dashboard.Status);
    }

    // Flips the shared default ServerConfig row for the test and restores it in finally, so no other test sees the change.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_GreenWhenProblemAlertsDisabled(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.AuthFailed, "test");

        ServerConfig original = await _repo.ServerConfigGetAsync();
        try
        {
            original.ProblemEventAlertsEnabled = false;
            await _repo.ServerConfigUpdateAsync(original);

            var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
            Assert.Equal(ZoneStatus.Green, dashboard.Status);
            Assert.Empty(dashboard.ProblemAlerts);
        }
        finally
        {
            original.ProblemEventAlertsEnabled = true;
            await _repo.ServerConfigUpdateAsync(original);
        }
    }

    // An event older than the configured ProblemEventExpiryHours stops counting, even inside the default 24h.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_GreenWhenProblemEventOlderThanConfiguredExpiry(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.AuthFailed, "test");

        await using (var db = _fx.NewContext(t))
        {
            var ev = await db.EventDevices.FirstAsync(e => e.DeviceID == d.IDDevice.Value);
            ev.Date = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }

        ServerConfig original = await _repo.ServerConfigGetAsync();
        try
        {
            original.ProblemEventExpiryHours = 1;
            await _repo.ServerConfigUpdateAsync(original);

            var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
            Assert.Equal(ZoneStatus.Green, dashboard.Status);
        }
        finally
        {
            original.ProblemEventExpiryHours = 24;
            await _repo.ServerConfigUpdateAsync(original);
        }
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_NoInternetEvent_DoesNotCountAsProblem(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });
        await _repo.EventDevicePushAsync(d.IDDevice.Value, tenantId, DeviceEventType.NoInternet, "test");

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Green, dashboard.Status);
    }

    // A disabled+offline device still shows a red "Offline" badge on its own row but must not redden a zone/unit nobody expects it to report into - it takes the zone/unit to Orange instead.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_DisabledOfflineDevice_TurnsOrange_NotRed(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeDevice(t, tenantId); // MakeDevice leaves Enabled at its false default
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        Assert.Equal(ZoneStatus.Orange, dashboard.Status);
    }

    // Covers a device that WAS online and has since gone stale (LastSeenAt pushed into the past directly, since DeviceDiagnosticUpsertAsync always stamps "now") - compares Fleet vs. the zone dashboard for the same device.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDashboard_Status_RedWhenEnabledDeviceWentStale_MatchesFleet(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeEnabledDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });

        await using (var db = _fx.NewContext(t))
        {
            var diag = await db.DeviceDiagnostics.FirstAsync(x => x.DeviceID == d.IDDevice.Value);
            diag.LastSeenAt = DateTime.UtcNow.AddHours(-2); // well past ComputeOnline's window at the default 60s SleepSeconds
            await db.SaveChangesAsync();
        }

        var fleet = Assert.Single(await _repo.DeviceFleetGetAsync(tenantId), f => f.IDDevice == d.IDDevice);
        Assert.False(fleet.Online, "Fleet should show this device offline");

        var dashboard = Assert.Single(await _repo.DeviceUnitDashboardGetAsync(tenantId), u => u.IDDeviceUnit == unit.IDDeviceUnit);
        var zoneDashboard = Assert.Single(await _repo.DeviceUnitZoneDashboardListGetAsync(unit.IDDeviceUnit!.Value));
        var zoneSingle = await _repo.DeviceUnitZoneDashboardGetAsync(zone.IDDeviceUnitZone!.Value);

        Assert.Equal(ZoneStatus.Red, dashboard.Status);
        Assert.Equal(ZoneStatus.Red, zoneDashboard.Status);
        Assert.Equal(ZoneStatus.Red, zoneSingle!.Status);
    }

    // No explicit dateCreated stamps UtcNow, landing in the trend's last bucket (index 23 = current hour).
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitZoneDashboard_Trend_BucketsRecentReadingIntoCurrentHour(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        await _repo.SensorDataPushAsync(new JsonArray(new JsonObject { ["temperature"] = "22.5" }),
            d.IDDevice!.Value, tenantId, unit.IDDeviceUnit, zone.IDDeviceUnitZone);

        var zoneDetail = await _repo.DeviceUnitZoneDashboardGetAsync(zone.IDDeviceUnitZone!.Value);
        Assert.NotNull(zoneDetail);
        Assert.Equal(22.5, zoneDetail!.Trend.Temperature[^1]);
        Assert.All(zoneDetail.Trend.Temperature.Take(zoneDetail.Trend.Temperature.Length - 1), Assert.Null);
    }

    // Deleting a Unit cascades its Zones and unassigns (not deletes) their devices.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUnitDelete_CascadesZones_AndUnassignsDevices_WithoutDeletingThem(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var (unit, zone) = await MakeUnitAndZone(tenantId);
        var d = await MakeDevice(t, tenantId);
        await _repo.DeviceAssignToZoneAsync(d.IDDevice!.Value, zone.IDDeviceUnitZone!.Value);

        await _repo.DeviceUnitDeleteAsync(unit.IDDeviceUnit!.Value);

        Assert.Null(await _repo.DeviceUnitGetByIdAsync(unit.IDDeviceUnit));
        Assert.Null(await _repo.DeviceUnitZoneGetByIdAsync(zone.IDDeviceUnitZone));
        var stillThere = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.NotNull(stillThere); // device itself is untouched
        Assert.Null(stillThere!.DeviceUnitID);
        Assert.Null(stillThere.DeviceUnitZoneID);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task EventDevicePush_InsertsWithCallerIdentity(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        bool inserted = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.NoInternet, "wifi dropped");
        Assert.True(inserted);

        var events = await _repo.EventDeviceGetAsync(d.IDDevice, tenantId);
        var ev = Assert.Single(events);
        Assert.Equal(d.IDDevice, ev.DeviceID);
        Assert.Equal(nameof(DeviceEventType.NoInternet), ev.EventType);
        Assert.Equal("wifi dropped", ev.Message);
        Assert.NotNull(ev.CreatedAt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task EventDevicePush_DedupesIdenticalEventWithinWindow_ButNotAfterItExpires(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        bool first = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.NoInternet, "1st");
        bool secondImmediate = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.NoInternet, "2nd, should be deduped");
        Assert.True(first);
        Assert.False(secondImmediate);
        Assert.Single(await _repo.EventDeviceGetAsync(d.IDDevice, tenantId));

        bool differentType = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.AuthFailed, "unrelated");
        Assert.True(differentType);
        Assert.Equal(2, (await _repo.EventDeviceGetAsync(d.IDDevice, tenantId)).Count);

        // Backdate past the default 10-minute dedupe window so the next push isn't suppressed.
        await using (var db = _fx.NewContext(t))
        {
            var row = await db.EventDevices.SingleAsync(e => e.DeviceID == d.IDDevice!.Value && e.EventID == (int)DeviceEventType.NoInternet);
            row.Date = DateTime.UtcNow.AddMinutes(-11);
            await db.SaveChangesAsync();
        }
        bool afterWindow = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.NoInternet, "3rd, window expired");
        Assert.True(afterWindow);
        Assert.Equal(3, (await _repo.EventDeviceGetAsync(d.IDDevice, tenantId)).Count);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task EventDeviceGet_OnlyReturnsSameTenantEvents(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantA, _, _) = await MakeUser(t);
        var deviceA = await MakeDevice(t, tenantA);
        var (tenantB, _, _) = await MakeUser(t);
        var deviceB = await MakeDevice(t, tenantB);

        await _repo.EventDevicePushAsync(deviceA.IDDevice!.Value, tenantA, DeviceEventType.OtaFailed, "tenant A");
        await _repo.EventDevicePushAsync(deviceB.IDDevice!.Value, tenantB, DeviceEventType.OtaFailed, "tenant B");

        var forA = await _repo.EventDeviceGetAsync(deviceA.IDDevice, tenantA);
        Assert.Single(forA);
        Assert.Equal("tenant A", forA[0].Message);

        var wrongTenant = await _repo.EventDeviceGetAsync(deviceA.IDDevice, tenantB);
        Assert.Empty(wrongTenant);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataGet_NullsOutlierCo2_ButKeepsTheRestOfTheRow_AndWritesReport(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // Anchor to mid-minute so the +/- second offsets below can never straddle a minute boundary.
        DateTime n = DateTime.Now;
        var thisMinute = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 30);
        var minuteBefore = thisMinute.AddMinutes(-1);
        var twoMinutesBefore = thisMinute.AddMinutes(-2);

        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                // <=400 (CCS811 "not warmed up yet" sentinel) - CO2 must null out, Temperature must survive.
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 250, Temperature = 10, DateCreated = twoMinutesBefore },
                // >=8000 (outlier/bad reading) - same expectation.
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 9000, Temperature = 20, DateCreated = minuteBefore },
                // Genuinely in range - passes through unchanged.
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 4000, Temperature = 30, DateCreated = thisMinute });
            await db.SaveChangesAsync();
        }

        string json = await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 0, 1);
        var arr = JsonDocument.Parse(json).RootElement.GetProperty("sensorData");

        // All 3 buckets present - a device with no working CO2 reading this minute must not lose its whole row, only the co2 field.
        Assert.Equal(3, arr.GetArrayLength());

        Assert.Equal(10, arr[0].GetProperty("temperature").GetDouble());
        Assert.Equal(JsonValueKind.Null, arr[0].GetProperty("co2").ValueKind);

        Assert.Equal(20, arr[1].GetProperty("temperature").GetDouble());
        Assert.Equal(JsonValueKind.Null, arr[1].GetProperty("co2").ValueKind);

        Assert.Equal(30, arr[2].GetProperty("temperature").GetDouble());
        Assert.Equal(4000, arr[2].GetProperty("co2").GetInt32());

        await using var db2 = _fx.NewContext(t);
        Assert.True(await db2.SensorDataReports.AnyAsync(r => r.DeviceID == d.IDDevice && r.SensorData == json));

        Assert.Equal("", await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 7, 0));
    }

    // Roadmap #301: a caller-supplied (timeRange, timeMDMY) is otherwise unbounded - a request for "100 years" would load the device's entire history into memory. The clamp caps the effective cutoff without erroring, so a request beyond it just returns less than asked rather than everything.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataGet_ClampsAnUnreasonablyLargeTimeRange_ToTheSafetyCeiling(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        DateTime now = DateTime.UtcNow;
        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                // Well beyond the safety ceiling (400 days) - must be excluded even though the request below asks for 100 years.
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Temperature = 999, DateCreated = now.AddYears(-5) },
                // Inside the ceiling - must still come back.
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Temperature = 21, DateCreated = now.AddDays(-30) });
            await db.SaveChangesAsync();
        }

        string json = await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 100, 3, 0); // 100 years
        var arr = JsonDocument.Parse(json).RootElement.GetProperty("sensorData");

        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal(21, arr[0].GetProperty("temperature").GetDouble());
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataReportGet_Metadata_Then_Full_Row_TenantScoped(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        await using (var db = _fx.NewContext(t))
        {
            db.SensorDataReports.Add(new SensorDataReportRow { DeviceID = d.IDDevice, ReportName = "r1", SensorData = "{\"sensorData\":[]}", DateGenerated = DateTime.Now });
            await db.SaveChangesAsync();
        }

        var meta = await _repo.SensorDataReportGetAsync(tenantId, 0, d.IDDevice, null);
        var one = Assert.Single(meta);
        Assert.Equal("r1", one.ReportName);
        Assert.Null(one.SensorData);

        // deviceID null lists every report in the tenant (the Reporting page), not just this one device.
        var everyReport = await _repo.SensorDataReportGetAsync(tenantId, 0, null, null);
        Assert.Contains(everyReport, r => r.IDSensorDataReport == one.IDSensorDataReport);
        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId + 999, 0, null, null));

        var full = await _repo.SensorDataReportGetAsync(tenantId, 1, null, one.IDSensorDataReport);
        Assert.Equal("{\"sensorData\":[]}", Assert.Single(full).SensorData);

        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId + 999, 0, d.IDDevice, null));
        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId, -1, d.IDDevice, null));
    }

    /// DB UTC rows shaped to JSON, then dateCreated localized for display: chart payload shifts by the user's zone, or passes UTC through untouched if null.
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataGet_Then_Localize_Shifts_Chart_Dates_By_User_Zone(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        DateTime n = DateTime.UtcNow;
        var utcStamp = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 30);
        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.Add(new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 400, Temperature = 20, DateCreated = utcStamp });
            await db.SaveChangesAsync();
        }

        string json = await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 0, 0);
        Assert.Contains(utcStamp.ToString("yyyy-MM-dd HH:mm:ss"), json);

        string? localized = api.Utils.SensorDataTimeLocalizer.LocalizeDates(json, "Europe/Zagreb");
        var expectedLocal = api.Utils.TimeZoneHelper.ToUserLocalTime(utcStamp, "Europe/Zagreb");
        Assert.NotEqual(utcStamp, expectedLocal); // Zagreb is never UTC+0, so the shift must show
        Assert.Contains(expectedLocal.ToString("yyyy-MM-dd HH:mm:ss"), localized);

        Assert.Equal(json, api.Utils.SensorDataTimeLocalizer.LocalizeDates(json, null));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataDelete_Removes_Rows_Older_Than_Cutoff(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        var now = DateTime.Now;

        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-10) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        await _repo.SensorDataDeleteAsync(tenantId, d.IDDevice, 5, 1);

        await using var db2 = _fx.NewContext(t);
        var left = await db2.SensorData.Where(r => r.DeviceID == d.IDDevice).ToListAsync();
        Assert.Single(left);
        Assert.True(left[0].DateCreated > now.AddDays(-2));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task OptimizeOldSensorData_Downsamples_5Minute_Bucket_ExcludingOutliers(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        DateTime cutoffUtc = DateTime.UtcNow.AddDays(-30);
        DateTime rawTimestamp = cutoffUtc.AddDays(-1);
        DateTime bucketStart = new(rawTimestamp.Ticks - (rawTimestamp.Ticks % TimeSpan.FromMinutes(5).Ticks), DateTimeKind.Utc);
        DateTime recentTimestamp = DateTime.UtcNow.AddDays(-1); // newer than cutoff - must survive untouched

        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 20, DateCreated = bucketStart.AddSeconds(10) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 21, DateCreated = bucketStart.AddSeconds(70) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 19, DateCreated = bucketStart.AddSeconds(130) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 20, DateCreated = bucketStart.AddSeconds(190) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 500, DateCreated = bucketStart.AddSeconds(250) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DeviceUnitID = 0, DeviceUnitZoneID = 0, Temperature = 99, DateCreated = recentTimestamp });
            await db.SaveChangesAsync();
        }

        await _repo.OptimizeOldSensorDataAsync(cutoffUtc, CancellationToken.None);

        await using var back = _fx.NewContext(t);
        var rows = await back.SensorData.Where(r => r.DeviceID == d.IDDevice).OrderBy(r => r.DateCreated).ToListAsync();

        var optimized = Assert.Single(rows, r => r.DateCreated == bucketStart);
        Assert.Equal(tenantId, optimized.TenantID);
        Assert.Equal(20.0, optimized.Temperature); // (19+20+20+21)/4 - the 500 outlier excluded by IQR

        // Identified by its distinct Temperature value, not exact DateCreated - avoids cross-provider microsecond-precision mismatches.
        var untouched = Assert.Single(rows, r => r.Temperature == 99);
        Assert.True(Math.Abs((untouched.DateCreated!.Value - recentTimestamp).TotalSeconds) < 1);
        Assert.Equal(2, rows.Count); // one optimized bucket + the untouched recent row
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task PurgeOldSensorData_DeletesRows_Older_Than_Cutoff(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        var now = DateTime.UtcNow;

        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-10) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        await _repo.PurgeOldSensorDataAsync(now.AddDays(-5), shrinkAfterPurge: false, CancellationToken.None);

        await using var back = _fx.NewContext(t);
        var left = await back.SensorData.Where(r => r.DeviceID == d.IDDevice).ToListAsync();
        Assert.Single(left);
        Assert.True(left[0].DateCreated > now.AddDays(-2));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ClassifyException_On_Real_Missing_Table_Is_SchemaMissing(DbProviderKind provider)
    {
        var t = Use(provider);
        await using var db = _fx.NewContext(t);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT * FROM table_that_is_not_there_" + U());
            Assert.Fail("expected the query to throw");
        }
        catch (Exception ex)
        {
            Assert.Equal(DbFailureKind.SchemaMissing, _repo.ClassifyException(ex));
        }
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task ClassifyException_On_Real_Unique_Violation_Is_ConstraintViolation(DbProviderKind provider)
    {
        var t = Use(provider);
        string name = "T_" + U();
        await _repo.TenantAddAsync(name);

        await using var db = _fx.NewContext(t);
        db.Tenants.Add(new TenantRow { TenantName = name }); // collides with tenant.Name_UNIQUE
        try
        {
            await db.SaveChangesAsync();
            Assert.Fail("expected the duplicate insert to throw");
        }
        catch (Exception ex)
        {
            Assert.Equal(DbFailureKind.ConstraintViolation, _repo.ClassifyException(ex));
        }
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserActivateAsync_ValidToken_SetsEmailVerifiedAndClearsToken(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string tokenHash = "hash-" + U(); // unique per run - the suite has no teardown and the column has a unique index
        await _repo.UserSetActivationTokenAsync(userId, tokenHash, DateTime.UtcNow.AddHours(1));

        User? activated = await _repo.UserActivateAsync(tokenHash);

        Assert.NotNull(activated);
        Assert.Equal(userId, activated!.IDUser);
        Assert.True(activated.EmailVerified);

        User? secondAttempt = await _repo.UserActivateAsync(tokenHash);
        Assert.Null(secondAttempt);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserActivateAsync_ExpiredToken_ReturnsNull_LeavesEmailUnverified(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, email) = await MakeUser(t);
        string tokenHash = "expired-" + U(); // stays in the DB forever (expiry never clears it), so it MUST be run-unique
        await _repo.UserSetActivationTokenAsync(userId, tokenHash, DateTime.UtcNow.AddHours(-1)); // already in the past

        User? result = await _repo.UserActivateAsync(tokenHash);

        Assert.Null(result);
        User? stillPending = await _repo.UserGetAsync(null, email, null);
        Assert.False(stillPending!.EmailVerified);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserIssueActivationTokenAsync_AlreadyVerified_ReturnsFalse(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        string firstToken = "first-" + U();
        await _repo.UserSetActivationTokenAsync(userId, firstToken, DateTime.UtcNow.AddHours(1));
        Assert.NotNull(await _repo.UserActivateAsync(firstToken)); // now EmailVerified

        bool issued = await _repo.UserIssueActivationTokenAsync(userId, "second-" + U(), DateTime.UtcNow.AddHours(1), cooldownMinutes: 0);

        Assert.False(issued);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserIssueActivationTokenAsync_WithinCooldown_ReturnsFalse_ButOffCooldown_ReturnsTrue(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        await _repo.UserSetActivationTokenAsync(userId, "initial-" + U(), DateTime.UtcNow.AddHours(1)); // sets ActivationLastSentAt=now

        bool tooSoon = await _repo.UserIssueActivationTokenAsync(userId, "resend1-" + U(), DateTime.UtcNow.AddHours(1), cooldownMinutes: 10);
        Assert.False(tooSoon);

        // Backdate ActivationLastSentAt past the cooldown window directly instead of waiting 10 minutes.
        await using (var db = _fx.NewContext(t))
        {
            var row = await db.Users.FirstAsync(u => u.IDUser == userId);
            row.ActivationLastSentAt = DateTime.UtcNow.AddMinutes(-11);
            await db.SaveChangesAsync();
        }

        // Re-Use() for a fresh, untracked context/repo, the same way a new HTTP request would - otherwise this read would return the stale tracked instance instead of re-querying the DB.
        Use(provider);
        string resend2 = "resend2-" + U();
        bool offCooldown = await _repo.UserIssueActivationTokenAsync(userId, resend2, DateTime.UtcNow.AddHours(1), cooldownMinutes: 10);
        Assert.True(offCooldown);

        Assert.NotNull(await _repo.UserActivateAsync(resend2));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task TenantAdminsGetAsync_ReturnsOnlyAdminsOfThatTenant(DbProviderKind provider)
    {
        var t = Use(provider);
        string tag = U();
        int tenantId = await _repo.TenantAddAsync("T_" + tag);

        var admin = new User { TenantID = tenantId, Email = tag + "-admin@ex.com", Username = "admin_" + tag, DevicePin = "PIN2A2" };
        var regular = new User { TenantID = tenantId, Email = tag + "-user@ex.com", Username = "user_" + tag, DevicePin = "PIN2B2" };
        await _repo.UserAddAsync(admin, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        await _repo.UserAddAsync(regular, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        var adminBack = await _repo.UserGetAsync(null, admin.Email, null);
        await _repo.UserRolesSetAsync(adminBack!.IDUser!.Value, new[] { RoleNames.TenantAdmin });

        var (_, _, _) = await MakeUser(t); // creates its own tenant + a regular user, unrelated
        int otherTenantId = await _repo.TenantAddAsync("T_" + U());
        var otherAdmin = new User { TenantID = otherTenantId, Email = U() + "@ex.com", Username = "u_" + U(), DevicePin = "PIN2C2" };
        await _repo.UserAddAsync(otherAdmin, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        var otherAdminBack = await _repo.UserGetAsync(null, otherAdmin.Email, null);
        await _repo.UserRolesSetAsync(otherAdminBack!.IDUser!.Value, new[] { RoleNames.TenantAdmin });

        IList<User> admins = await _repo.TenantAdminsGetAsync(tenantId);

        Assert.Single(admins);
        Assert.Equal(admin.Email, admins[0].Email);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UsersGetAllAsync_ReturnsUsersAcrossDifferentTenants(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantA, userIdA, _) = await MakeUser(t);
        var (tenantB, userIdB, _) = await MakeUser(t);
        Assert.NotEqual(tenantA, tenantB);

        IList<User> all = await _repo.UsersGetAllAsync();

        Assert.Contains(all, u => u.IDUser == userIdA);
        Assert.Contains(all, u => u.IDUser == userIdB);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserRoleNamesGetAsync_NewUser_IsEmpty(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);

        IReadOnlyList<string> roles = await _repo.UserRoleNamesGetAsync(userId);

        Assert.Empty(roles);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserRolesSetAsync_AssignsThenReplacesTheWholeSet(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);

        await _repo.UserRolesSetAsync(userId, new[] { RoleNames.TenantReader, RoleNames.TenantDevice });
        Assert.Equal(
            new[] { RoleNames.TenantReader, RoleNames.TenantDevice }.OrderBy(x => x),
            (await _repo.UserRoleNamesGetAsync(userId)).OrderBy(x => x));

        await _repo.UserRolesSetAsync(userId, new[] { RoleNames.TenantReader, RoleNames.TenantUser });
        Assert.Equal(
            new[] { RoleNames.TenantReader, RoleNames.TenantUser }.OrderBy(x => x),
            (await _repo.UserRoleNamesGetAsync(userId)).OrderBy(x => x));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserRolesSetAsync_EmptySet_ClearsEveryRole(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userId, _) = await MakeUser(t);
        await _repo.UserRolesSetAsync(userId, new[] { RoleNames.TenantAdmin });

        await _repo.UserRolesSetAsync(userId, Array.Empty<string>());

        Assert.Empty(await _repo.UserRoleNamesGetAsync(userId));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task UserRolesSetAsync_DifferentUsers_DoNotShareRoles(DbProviderKind provider)
    {
        var t = Use(provider);
        var (_, userIdA, _) = await MakeUser(t);
        var (_, userIdB, _) = await MakeUser(t);

        await _repo.UserRolesSetAsync(userIdA, new[] { RoleNames.GlobalAdmin });

        Assert.Equal(new[] { RoleNames.GlobalAdmin }, await _repo.UserRoleNamesGetAsync(userIdA));
        Assert.Empty(await _repo.UserRoleNamesGetAsync(userIdB));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DevicesGetAllAsync_ReturnsDevicesAcrossDifferentTenants(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantA, _, _) = await MakeUser(t);
        var (tenantB, _, _) = await MakeUser(t);
        Device deviceA = await MakeDevice(t, tenantA);
        Device deviceB = await MakeDevice(t, tenantB);

        IList<Device> all = await _repo.DevicesGetAllAsync();

        Assert.Contains(all, d => d.IDDevice == deviceA.IDDevice);
        Assert.Contains(all, d => d.IDDevice == deviceB.IDDevice);
    }
}
