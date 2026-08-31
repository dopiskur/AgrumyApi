using System.Text.Json;
using System.Text.Json.Nodes;
using api.Dal;
using api.Dal.Entities;
using api.Models;
using api.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>
/// End-to-end tests for <see cref="EfRepository"/> against a real database, run against every
/// engine that is configured (roadmap #42 - MySQL/MariaDB in Phase 1, PostgreSQL added in Phase 2).
/// A test is skipped for an engine whose env var is unset:
///   <c>AGRUMY_TEST_MYSQL</c>    e.g. server=127.0.0.1;port=33306;database=agrumyapi;user id=root;password=rootpw;
///   <c>AGRUMY_TEST_POSTGRES</c> e.g. Host=127.0.0.1;Port=55432;Database=agrumyapi;Username=postgres;Password=postgres
///
/// Throwaway containers:
/// <code>
///   docker run -d --name agrumy-my -e MARIADB_ROOT_PASSWORD=rootpw -p 33306:3306 mariadb:11.4
///   docker run -d --name agrumy-pg -e POSTGRES_PASSWORD=postgres  -p 55432:5432 postgres:17
/// </code>
///
/// The fixture creates each engine's schema from the model (EnsureCreated, matching the
/// pre-beta EfRepository.EnsureSchemaAsync) and seeds the reference rows the proc-era inner
/// joins need; every test uses GUID-unique keys so there is no teardown.
/// </summary>
public sealed class RelationalIntegrationFixture
{
    public sealed record Target(DbProviderKind Provider, string ConnectionString,
        int RegularGroupId, int AdminGroupId, int DeviceTypeId);

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

        if (!db.UserRoles.Any())
        {
            var rAdmin = new UserRoleRow { RoleName = "admin" };
            var rUser = new UserRoleRow { RoleName = "user" };
            db.UserRoles.AddRange(rAdmin, rUser);
            db.SaveChanges();
            db.UserGroups.AddRange(
                new UserGroupRow { GroupName = "users", UserRoleID = rUser.IDUserRole },
                new UserGroupRow { GroupName = "admins", UserRoleID = rAdmin.IDUserRole });
            db.SaveChanges();
        }

        int regular = db.UserGroups.Where(g => g.GroupName == "users").Select(g => g.IDUserGroup).First();
        int admin = db.UserGroups.Where(g => g.GroupName == "admins").Select(g => g.IDUserGroup).First();

        // Roadmap #66: the composable role catalog - a fresh test DB has none of these, same
        // reasoning as the admin/user seeding above (EnsureCreated only makes tables, never rows).
        if (!db.UserRoles.Any(r => r.RoleName == RoleNames.TenantReader))
        {
            db.UserRoles.AddRange(RoleNames.All.Select(name => new UserRoleRow { RoleName = name }));
            db.SaveChanges();
        }

        int deviceType = db.DeviceTypes.Where(t => t.DeviceTypeName == "greenhouse")
                           .Select(t => (int?)t.IDDeviceType).FirstOrDefault() ?? SeedDeviceType(db);

        // deviceUnit(0)/deviceUnitZone(0) are the "Default"/"Disabled" sentinel rows the production
        // DB ships (db/agrumyDB-withData.sql); sensorData's non-null DeviceUnitID/DeviceUnitZoneID
        // default to 0, so with the legacy FKs in place these must exist. Zone before unit.
        if (!db.DeviceUnitZones.Any())
            db.DeviceUnitZones.Add(new DeviceUnitZoneRow { IDDeviceUnitZone = 0, DeviceUnitZoneName = "Disabled" });
        db.SaveChanges();
        if (!db.DeviceUnits.Any())
            db.DeviceUnits.Add(new DeviceUnitRow { IDDeviceUnit = 0, DeviceUnitZoneID = null, DeviceUnitName = "Default" });

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

        _targets[provider] = new Target(provider, conn, regular, admin, deviceType);
    }

    private static int SeedDeviceType(AgrumyDbContext db)
    {
        var t = new DeviceTypeRow { DeviceTypeName = "greenhouse" };
        db.DeviceTypes.Add(t);
        db.SaveChanges();
        return t.IDDeviceType;
    }

    public AgrumyDbContext NewContext(Target t) => new(DbOptionsFactory.Build(t.Provider, t.ConnectionString));
}

[Collection("RepoFactory")] // shares the process-wide EfRepository.ProviderOverride / ConnectionStringOverride seams
public sealed class RelationalIntegrationTests : IClassFixture<RelationalIntegrationFixture>, IDisposable
{
    private readonly RelationalIntegrationFixture _fx;
    private readonly EfRepository _repo = new();

    public RelationalIntegrationTests(RelationalIntegrationFixture fx) => _fx = fx;

    public void Dispose()
    {
        EfRepository.ProviderOverride = null;
        EfRepository.ConnectionStringOverride = null;
    }

    /// <summary>One row per configured engine, or a sentinel that makes every test skip.</summary>
    public static IEnumerable<object[]> Providers()
    {
        var rows = new List<object[]>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRUMY_TEST_MYSQL")))
            rows.Add(new object[] { DbProviderKind.MySql });
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRUMY_TEST_POSTGRES")))
            rows.Add(new object[] { DbProviderKind.Postgres });
        return rows.Count > 0 ? rows : new[] { new object[] { (DbProviderKind)255 } };
    }

    private RelationalIntegrationFixture.Target Use(DbProviderKind provider)
    {
        var t = _fx.Targets.FirstOrDefault(x => x.Provider == provider);
        Skip.If(t is null, $"No integration database configured for {provider}.");
        EfRepository.ProviderOverride = t!.Provider;
        EfRepository.ConnectionStringOverride = t.ConnectionString;
        return t;
    }

    private static string U() => Guid.NewGuid().ToString("N")[..12];

    private async Task<(int tenantId, int userId, string email)> MakeUser(RelationalIntegrationFixture.Target t, bool enabled = true)
    {
        string tag = U();
        int tenantId = await _repo.TenantAddAsync("T_" + tag);
        var user = new User
        {
            TenantID = tenantId,
            UserGroupID = t.RegularGroupId,
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

    // ---- schema -------------------------------------------------------------

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

    // roadmap #29: the pre-EF SchemaScripts.cs created tables as latin1. The EF model sets no
    // charset, so a fresh MySQL/MariaDB database built by EnsureCreatedAsync must come out
    // utf8mb4 (Pomelo applies an implicit HasCharSet("utf8mb4")). Guards against a regression
    // that would silently corrupt non-ASCII names. MySQL-only.
    [SkippableFact]
    public async Task Fresh_MySql_Schema_Is_Utf8mb4_Not_Latin1()
    {
        var t = Use(DbProviderKind.MySql); // Skip.If when AGRUMY_TEST_MYSQL is unset
        await using var db = _fx.NewContext(t);

        var tableCharsets = await db.Database.SqlQueryRaw<string>(
            "SELECT SUBSTRING_INDEX(TABLE_COLLATION, '_', 1) AS Value FROM information_schema.TABLES " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'").ToListAsync();
        Assert.NotEmpty(tableCharsets);
        Assert.All(tableCharsets, cs => Assert.Equal("utf8mb4", cs));

        // Every actual string column, not just the table default - and nothing left on latin1/utf8mb3.
        var columnCharsets = await db.Database.SqlQueryRaw<string>(
            "SELECT DISTINCT CHARACTER_SET_NAME AS Value FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND CHARACTER_SET_NAME IS NOT NULL").ToListAsync();
        Assert.Equal(new[] { "utf8mb4" }, columnCharsets);
    }

    // ---- tenant / server config -------------------------------------------

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

    // ---- refresh tokens -----------------------------------------------------

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

        await _repo.RefreshTokenRotateAsync(oldHash, newHash, DateTime.UtcNow.AddDays(30));

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

        // Simulates a stolen, already-used token being replayed: rotating it again must not
        // resurrect it or silently create a usable replacement.
        await _repo.RefreshTokenRotateAsync(hash, U(), DateTime.UtcNow.AddDays(30));

        var stillRevoked = await _repo.RefreshTokenGetAsync(hash);
        Assert.NotNull(stillRevoked!.RevokedAt);
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

    // ---- user -------------------------------------------------------------

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task User_Add_Then_Get_By_Every_Key_WithGroupJoin(DbProviderKind provider)
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
        Assert.Equal("users", byId.GroupName);
        Assert.NotNull(byId.UserRoleID);
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

    /// <summary>Regression gate for the self-service profile endpoint: UserProfileSetAsync must
    /// write ONLY FirstName/LastName/TimeZone - if it ever starts touching Enabled/UserGroupID/
    /// TenantID (or the password), a user could self-escalate through PUT /api/User/Profile.</summary>
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

        // Everything authorization-bearing stays exactly as MakeUser created it.
        Assert.Equal(tenantId, back.TenantID);
        Assert.Equal(t.RegularGroupId, back.UserGroupID);
        Assert.True(back.Enabled);
        var secret = await _repo.UserSecretGetAsync(userId, null, null);
        Assert.NotNull(secret);
        Assert.Equal("h", secret.PwdHash);

        // Clearing the zone (back to "no preference" = UTC display) round-trips as null.
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
            FirstName = "New", LastName = "Name", Phone = "999", UserGroupID = t.RegularGroupId, Enabled = false, DevicePin = "HACKED",
        });

        var back = await _repo.UserGetAsync(userId, null, null);
        Assert.NotNull(back);
        Assert.Equal("New", back.FirstName);
        Assert.Equal(7, back.TenantID);
        Assert.False(back.Enabled);
        // Roadmap #70: UserUpdateAsync must never touch the PIN - its lifecycle belongs solely to
        // UserSetDevicePinAsync, so the MakeUser value survives the update above unchanged.
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

        // Roadmap #70 follow-up: a successful device registration no longer calls this - the PIN
        // is multi-use within its own expiry. Nulls remain a supported explicit-clear operation.
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
    public async Task UserRole_And_UserGroup_CRUD_WithRoleJoin(DbProviderKind provider)
    {
        var t = Use(provider);
        var roles = await _repo.UserRoleGetAsync();
        Assert.Contains(roles, r => r.RoleName == "admin");

        string gname = "G_" + U();
        await _repo.UserGroupAddAsync(new UserGroup { GroupName = gname, UserRoleID = roles.First(r => r.RoleName == "admin").IDUserRole });

        var all = await _repo.UserGroupsGetAsync();
        var mine = Assert.Single(all, g => g.GroupName == gname);
        Assert.Equal("admin", mine.RoleName);

        var one = await _repo.UserGroupGetAsync(mine.IDUserGroup);
        Assert.NotNull(one);
        Assert.Equal("admin", one.RoleName);

        await _repo.UserGroupDeleteAsync(0);
        await _repo.UserGroupDeleteAsync(mine.IDUserGroup);
        Assert.Null(await _repo.UserGroupGetAsync(mine.IDUserGroup));
    }

    // ---- device ---------------------------------------------------------

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
        // DeviceGetByApiIdAsync has no tenant filter (device-comm endpoints have no tenant context).
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByApiIdAsync(d.ApiId))!.IDDevice);
        Assert.Null(await _repo.DeviceGetByApiIdAsync("no-such-api-id-" + U()));
        Assert.Single(await _repo.DevicesGetAsync(tenantId));
        Assert.True(await _repo.DeviceCheckMacAddressAsync(tenantId, d.MacAddress));
        Assert.False(await _repo.DeviceCheckMacAddressAsync(tenantId, "no_" + U()));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceUpdate_Sets_ConfigVersion_To_Payload_Plus_One(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        d.DeviceName = "renamed";
        d.ConfigVersion = 40;
        await _repo.DeviceUpdateAsync(d);

        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.NotNull(back);
        Assert.Equal("renamed", back.DeviceName);
        Assert.Equal(41, back.ConfigVersion);
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
            IDDeviceConfigController = d.DeviceConfigControllerID, TempLow = 5.5, TempHigh = 30.25, RelayEnabled = true, Relay1 = 2,
        });
        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.NotNull(back);
        Assert.Equal(v0 + 2, back.ConfigVersion);

        var ctrl = await _repo.DeviceConfigControllerGetAsync(d.DeviceConfigControllerID);
        Assert.Equal(5.5, ctrl!.TempLow);   // real double (proc truncated via int params)
        Assert.Equal(30.25, ctrl.TempHigh);
        Assert.True(ctrl.RelayEnabled);
        Assert.Equal(1, (await _repo.DeviceConfigSensorGetAsync(d.DeviceConfigSensorID))!.SensorTemp);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceDelete_Removes_Device_And_Its_Config_Rows(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // A diagnostic row must not block the delete (its FK to device is NoAction, roadmap #7).
        await _repo.DeviceDiagnosticUpsertAsync(d.IDDevice!.Value, tenantId, new DeviceConfigPoll { ConfigVersion = 1 });

        await _repo.DeviceDeleteAsync(d.IDDevice, tenantId);

        Assert.Null(await _repo.DeviceGetByIdAsync(d.IDDevice));
        await using var db = _fx.NewContext(t);
        Assert.False(await db.DeviceConfigSensors.AnyAsync(c => c.IDDeviceConfigSensor == d.DeviceConfigSensorID));
        Assert.False(await db.DeviceConfigControllers.AnyAsync(c => c.IDDeviceConfigController == d.DeviceConfigControllerID));
        Assert.False(await db.DeviceDiagnostics.AnyAsync(x => x.DeviceID == d.IDDevice));
    }

    // ---- device diagnostics / fleet (roadmap #7 + #8) -----------------------

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task DeviceDiagnostic_Upsert_Records_Heartbeat_And_Fleet_Reports_It(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // Never-seen device still shows on the dashboard, as offline.
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

        // A pre-#7 poll (ConfigVersion only) bumps LastSeenAt but keeps the earlier diagnostics.
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
        // Roadmap #40: OfflineAlertCandidatesGetAsync uses two correlated subqueries (LastSeenAt,
        // OfflineNotifiedAt) and DeviceOfflineNotifiedSetAsync uses ExecuteUpdateAsync - both need
        // real translation-correctness verification against each provider, not just a mock.
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);
        // MakeDevice leaves Enabled null - OfflineAlertCandidatesGetAsync only considers Enabled ==
        // true (same "null reads as disabled" convention Fleet.cshtml's badge already uses).
        d.Enabled = true;
        await _repo.DeviceUpdateAsync(d);

        // Never-seen device: still a candidate (enabled), but with null LastSeenAt/OfflineNotifiedAt.
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
    public async Task DeviceType_Lists_Return_Seeded_Rows(DbProviderKind provider)
    {
        var t = Use(provider);
        Assert.Contains(await _repo.DeviceTypeGetAsync(), x => x.IDDeviceType == t.DeviceTypeId);
        Assert.Contains(await _repo.DeviceTypeServiceGetAsync(), x => x.IDDeviceTypeService == 1);
        Assert.Contains(await _repo.DeviceTypeRelayGetAsync(), x => x.IDDeviceTypeRelay == 1);
        Assert.Contains(await _repo.DeviceTypeSensorGetAsync(), x => x.IDDeviceTypeSensor == 1);
    }

    // ---- sensor data --------------------------------------------------

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
        // UtcNow, matching SensorDataPushAsync's UTC fallback (roadmap #71) — local Now would be ahead of it.
        Assert.True(rows[1].DateCreated > DateTime.UtcNow.AddMinutes(-5));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataPush_Uses_Caller_Identity_And_Ignores_Payload_Ids(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // Every id in the payload is a lie - a different device, tenant, unit and zone.
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

    // ---- device events (roadmap #28) ----------------------------------

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

        // Push a different event type in between - must never dedupe against an unrelated type.
        bool differentType = await _repo.EventDevicePushAsync(d.IDDevice!.Value, tenantId, DeviceEventType.AuthFailed, "unrelated");
        Assert.True(differentType);
        Assert.Equal(2, (await _repo.EventDeviceGetAsync(d.IDDevice, tenantId)).Count);

        // Backdate the NoInternet row past the default 10-minute dedupe window and confirm the
        // next push of the same type is no longer suppressed.
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

        // Same deviceID passed under the WRONG tenant must not leak tenant A's row.
        var wrongTenant = await _repo.EventDeviceGetAsync(deviceA.IDDevice, tenantB);
        Assert.Empty(wrongTenant);
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataGet_Buckets_Rows_Excludes_Null_Co2_And_Writes_Report(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // Anchor to mid-minute so the +/- second offsets below can never straddle a minute
        // boundary (bucket mode 0 groups by minute).
        DateTime n = DateTime.Now;
        var thisMinute = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 30);
        var prevMinute = thisMinute.AddMinutes(-1);

        await using (var db = _fx.NewContext(t))
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 400, Temperature = 1, DateCreated = prevMinute.AddSeconds(-5) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 401, Temperature = 2, DateCreated = prevMinute.AddSeconds(5) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 402, Temperature = 3, DateCreated = thisMinute.AddSeconds(-5) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = null, Temperature = 99, DateCreated = thisMinute.AddSeconds(-4) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 9000, Temperature = 88, DateCreated = thisMinute.AddSeconds(-3) });
            await db.SaveChangesAsync();
        }

        string json = await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 0, 1);
        var arr = JsonDocument.Parse(json).RootElement.GetProperty("sensorData");

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(2, arr[0].GetProperty("temperature").GetDouble());
        Assert.Equal(3, arr[1].GetProperty("temperature").GetDouble());

        await using var db2 = _fx.NewContext(t);
        Assert.True(await db2.SensorDataReports.AnyAsync(r => r.DeviceID == d.IDDevice && r.SensorData == json));

        Assert.Equal("", await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 7, 0));
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

        var full = await _repo.SensorDataReportGetAsync(tenantId, 1, null, one.IDSensorDataReport);
        Assert.Equal("{\"sensorData\":[]}", Assert.Single(full).SensorData);

        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId + 999, 0, d.IDDevice, null));
        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId, -1, d.IDDevice, null));
    }

    /// <summary>The exact pipeline Agrumy.Web's SensorData views run (roadmap #71 follow-up):
    /// UTC rows from the DB, shaped to JSON, then dateCreated localized for display - proves the
    /// chart payload shifts by the user's zone while a null zone passes UTC through untouched.</summary>
    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task SensorDataGet_Then_Localize_Shifts_Chart_Dates_By_User_Zone(DbProviderKind provider)
    {
        var t = Use(provider);
        var (tenantId, _, _) = await MakeUser(t);
        var d = await MakeDevice(t, tenantId);

        // Mid-minute UTC anchor, same reasoning as the bucketing test above.
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

        // A user with no zone preference keeps the raw UTC payload byte-for-byte.
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

    // ---- error classification --------------------------------------

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

    // ---- Email activation (roadmap #24/#63) ----------------------------------

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

        // The token must not be redeemable a second time - UserActivateAsync clears it.
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

        // Backdate ActivationLastSentAt past the cooldown window directly, same technique the
        // EventDevicePush dedupe-expiry test uses - no need to actually wait 10 minutes.
        await using (var db = _fx.NewContext(t))
        {
            var row = await db.Users.FirstAsync(u => u.IDUser == userId);
            row.ActivationLastSentAt = DateTime.UtcNow.AddMinutes(-11);
            await db.SaveChangesAsync();
        }

        string resend2 = "resend2-" + U();
        bool offCooldown = await _repo.UserIssueActivationTokenAsync(userId, resend2, DateTime.UtcNow.AddHours(1), cooldownMinutes: 10);
        Assert.True(offCooldown);

        // The new token, not the stale one, must be the one that now activates the account.
        Assert.NotNull(await _repo.UserActivateAsync(resend2));
    }

    [SkippableTheory, MemberData(nameof(Providers))]
    public async Task TenantAdminsGetAsync_ReturnsOnlyAdminsOfThatTenant(DbProviderKind provider)
    {
        var t = Use(provider);
        string tag = U();
        int tenantId = await _repo.TenantAddAsync("T_" + tag);

        var admin = new User { TenantID = tenantId, UserGroupID = t.AdminGroupId, Email = tag + "-admin@ex.com", Username = "admin_" + tag, DevicePin = "PIN2A2" };
        var regular = new User { TenantID = tenantId, UserGroupID = t.RegularGroupId, Email = tag + "-user@ex.com", Username = "user_" + tag, DevicePin = "PIN2B2" };
        await _repo.UserAddAsync(admin, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        await _repo.UserAddAsync(regular, new UserSecret { PwdHash = "h", PwdSalt = "s" });

        // A tenant admin elsewhere must never show up for THIS tenant's lookup.
        var (_, _, _) = await MakeUser(t); // creates its own tenant + a regular user, unrelated
        int otherTenantId = await _repo.TenantAddAsync("T_" + U());
        var otherAdmin = new User { TenantID = otherTenantId, UserGroupID = t.AdminGroupId, Email = U() + "@ex.com", Username = "u_" + U(), DevicePin = "PIN2C2" };
        await _repo.UserAddAsync(otherAdmin, new UserSecret { PwdHash = "h", PwdSalt = "s" });

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

    // ---- Composable roles (roadmap #66) --------------------------------------

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

        // A second call REPLACES, not adds - dropping TenantDevice and adding TenantUser instead.
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
