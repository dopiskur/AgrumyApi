using System.Text.Json.Nodes;
using api.Dal;
using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>
/// End-to-end tests for <see cref="EfRepository"/> against a real MySQL/MariaDB (roadmap #42
/// Phase 1 verification). Skipped unless <c>AGRUMY_TEST_MYSQL</c> holds a connection string.
/// The fixture applies the EF baseline migration and seeds the reference rows the proc-era inner
/// joins depend on; each test uses GUID-unique keys so there is no teardown.
///
/// Run against a throwaway container:
/// <code>
///   docker run -d --name agrumy-ef-test -e MARIADB_ROOT_PASSWORD=rootpw \
///     -e MARIADB_DATABASE=agrumy -p 33306:3306 mariadb:11.4
///   set AGRUMY_TEST_MYSQL=server=127.0.0.1;port=33306;database=agrumy;user id=root;password=rootpw;
///   dotnet test Agrumy.Api.Tests
///   docker rm -f agrumy-ef-test
/// </code>
/// </summary>
public class MySqlIntegrationFixture
{
    public string? Conn { get; } = Environment.GetEnvironmentVariable("AGRUMY_TEST_MYSQL");
    public bool Enabled => !string.IsNullOrWhiteSpace(Conn);

    private static readonly object _gate = new();
    private static bool _ready;

    public MySqlIntegrationFixture()
    {
        if (!Enabled) return;

        EfRepository.ConnectionStringOverride = Conn;

        lock (_gate)
        {
            if (_ready) return;

            var opts = new DbContextOptionsBuilder<AgrumyDbContext>()
                .UseMySql(Conn!, new MariaDbServerVersion(new Version(11, 4, 0)))
                .Options;
            using var db = new AgrumyDbContext(opts);
            db.Database.Migrate();

            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO userRole (IDUserRole, RoleName) VALUES (1,'admin'),(2,'user')");
            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO userGroup (IDUserGroup, GroupName, UserRoleID) VALUES (1,'users',2),(2,'admins',1)");
            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO deviceType (IDDeviceType, DeviceTypeName) VALUES (1,'greenhouse')");
            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO deviceTypeService (IDDeviceTypeService, ServiceType) VALUES (1,'HTTPS')");
            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO deviceTypeRelay (IDDeviceTypeRelay, RelayName) VALUES (1,'pump')");
            db.Database.ExecuteSqlRaw("INSERT IGNORE INTO deviceTypeSensor (IDDeviceTypeSensor, SensorName) VALUES (1,'dht22')");

            _ready = true;
        }
    }

    public void Require()
    {
        Skip.IfNot(Enabled, "AGRUMY_TEST_MYSQL not set - integration tests skipped.");
    }

    internal AgrumyDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<AgrumyDbContext>()
            .UseMySql(Conn!, new MariaDbServerVersion(new Version(11, 4, 0)))
            .Options;
        return new AgrumyDbContext(opts);
    }
}

public class MySqlIntegrationTests : IClassFixture<MySqlIntegrationFixture>
{
    private readonly MySqlIntegrationFixture _fx;
    private readonly EfRepository _repo = new();

    public MySqlIntegrationTests(MySqlIntegrationFixture fx)
    {
        _fx = fx;
        _fx.Require();
    }

    private static string U() => Guid.NewGuid().ToString("N")[..12];

    private async Task<(int tenantId, int userId, string email)> MakeUser(bool enabled = true, int group = 1)
    {
        string tag = U();
        int tenantId = await _repo.TenantAddAsync("T_" + tag);
        var user = new User
        {
            TenantID = tenantId,
            UserGroupID = group,
            Email = tag + "@ex.com",
            Username = "u_" + tag,
            FirstName = "F",
            LastName = "L",
            Phone = "123",
            DevicePin = 4321,
            Enabled = enabled,
        };
        await _repo.UserAddAsync(user, new UserSecret { PwdHash = "h", PwdSalt = "s" });
        var back = await _repo.UserGetAsync(null, user.Email, null);
        return (tenantId, back.IDUser!.Value, user.Email!);
    }

    private async Task<Device> MakeDevice(int tenantId)
    {
        var d = new Device
        {
            TenantID = tenantId,
            DeviceTypeID = 1,
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
        return await _repo.DeviceGetAsync(tenantId, null, d.ApiId, null);
    }

    // ---- schema -------------------------------------------------------------

    [SkippableFact]
    public async Task Schema_HasEveryTable()
    {
        await using var db = _fx.NewContext();
        var tables = await db.Database.SqlQuery<string>(
            $"SELECT TABLE_NAME AS Value FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()")
            .ToListAsync();

        foreach (var t in new[] { "tenant", "user", "userGroup", "userRole", "userRoleScope",
            "device", "deviceUnit", "deviceUnitZone", "deviceType", "deviceTypeService",
            "deviceTypeRelay", "deviceTypeSensor", "deviceConfigSensor", "deviceConfigController",
            "deviceFirmware", "sensorData", "sensorDataReport", "eventDevice", "eventService",
            "serverConfig" })
        {
            Assert.Contains(t, tables);
        }
    }

    [SkippableFact]
    public async Task EnsureSchemaAsync_OnProvisionedDb_IsNoOpAndDoesNotThrow()
    {
        // Program.cs calls this on every startup; against a DB that already has tables (invent.hr,
        // and this container after the fixture migrated it) it must return without touching anything.
        await _repo.EnsureSchemaAsync();
        Assert.True(await _repo.TestConnectionAsync());
    }

    // ---- tenant / server config -------------------------------------------

    [SkippableFact]
    public async Task Tenant_Add_Get_GetId()
    {
        string name = "T_" + U();
        int id = await _repo.TenantAddAsync(name);

        Assert.True(id > 0);
        Assert.True(await _repo.TenantGetAsync(name));
        Assert.Equal(id, await _repo.TenantGetIdAsync(name));
        Assert.False(await _repo.TenantGetAsync("missing_" + U()));
        Assert.Null(await _repo.TenantGetIdAsync("missing_" + U()));
    }

    [SkippableFact]
    public async Task ServerConfig_AutoCreatesOnce()
    {
        int id = new Random().Next(1000, 9_000_000);
        var a = await _repo.ServerConfigGetAsync(id);
        var b = await _repo.ServerConfigGetAsync(id);

        Assert.Equal(id, a.IDServerConfig);
        Assert.False(string.IsNullOrWhiteSpace(a.ConfigKey));
        Assert.Equal(a.ConfigKey, b.ConfigKey); // second call reads, does not regenerate
        Assert.Equal(80, a.PortHTTP);
        Assert.Equal(443, a.PortHTTPS);
    }

    // ---- user -------------------------------------------------------------

    [SkippableFact]
    public async Task User_Add_Then_Get_By_Every_Key_WithGroupJoin()
    {
        var (tenantId, userId, email) = await MakeUser(group: 1);

        var byId = await _repo.UserGetAsync(userId, null, null);
        var byEmail = await _repo.UserGetAsync(null, email, null);
        var byName = await _repo.UserGetAsync(null, null, byId.Username);

        Assert.Equal(userId, byEmail.IDUser);
        Assert.Equal(userId, byName.IDUser);
        Assert.Equal(tenantId, byId.TenantID);
        Assert.Equal("users", byId.GroupName);   // from userGroup join
        Assert.Equal(2, byId.UserRoleID);        // userGroup(1).UserRoleID
    }

    [SkippableFact]
    public async Task User_Get_NoMatch_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserGetAsync(null, "nope_" + U() + "@x.com", null));
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserGetAsync(null, null, null));
    }

    [SkippableFact]
    public async Task UsersGet_ScopedToTenant()
    {
        var (tenantId, userId, _) = await MakeUser();
        var list = await _repo.UsersGetAsync(tenantId);

        Assert.Single(list);
        Assert.Equal(userId, list[0].IDUser);
        Assert.Empty(await _repo.UsersGetAsync(-999));
    }

    [SkippableFact]
    public async Task UserSecret_Get_And_SetPassword()
    {
        var (_, userId, email) = await MakeUser();

        var s = await _repo.UserSecretGetAsync(userId, null, null);
        Assert.Equal("h", s.PwdHash);
        Assert.Equal("s", s.PwdSalt);

        Assert.True(await _repo.UserSetPasswordAsync(email, new UserSecret { PwdHash = "h2", PwdSalt = "s2" }));
        Assert.False(await _repo.UserSetPasswordAsync("missing_" + U() + "@x.com", new UserSecret { PwdHash = "x", PwdSalt = "y" }));

        var s2 = await _repo.UserSecretGetAsync(null, email, null);
        Assert.Equal("h2", s2.PwdHash);

        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserSecretGetAsync(null, "missing_" + U() + "@x.com", null));
    }

    [SkippableFact]
    public async Task User_Update_Changes_Fields()
    {
        var (_, userId, _) = await MakeUser();

        await _repo.UserUpdateAsync(new User
        {
            IDUser = userId, TenantID = 7, Email = "upd_" + U() + "@x.com", Username = "n_" + U(),
            FirstName = "New", LastName = "Name", Phone = "999", UserGroupID = 1, Enabled = false, DevicePin = 1,
        });

        var back = await _repo.UserGetAsync(userId, null, null);
        Assert.Equal("New", back.FirstName);
        Assert.Equal(7, back.TenantID);
        Assert.False(back.Enabled);
    }

    [SkippableFact]
    public async Task User_Delete_Guard_And_Delete()
    {
        var (_, userId, _) = await MakeUser();

        Assert.False(await _repo.UserDeleteAsync(1));   // guard: id <= 1
        Assert.False(await _repo.UserDeleteAsync(null));
        Assert.True(await _repo.UserDeleteAsync(userId));
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserGetAsync(userId, null, null));
    }

    [SkippableFact]
    public async Task UserRole_And_UserGroup_CRUD_WithRoleJoin()
    {
        var roles = await _repo.UserRoleGetAsync();
        Assert.Contains(roles, r => r.RoleName == "admin");

        string gname = "G_" + U();
        await _repo.UserGroupAddAsync(new UserGroup { GroupName = gname, UserRoleID = 1 });

        var all = await _repo.UserGroupsGetAsync();
        var mine = Assert.Single(all, g => g.GroupName == gname);
        Assert.Equal("admin", mine.RoleName);   // userRole join

        var one = await _repo.UserGroupGetAsync(mine.IDUserGroup);
        Assert.Equal("admin", one.RoleName);

        await _repo.UserGroupDeleteAsync(0); // guard: id <= 0 -> no-op
        await _repo.UserGroupDeleteAsync(mine.IDUserGroup);
        await Assert.ThrowsAsync<ArgumentException>(() => _repo.UserGroupGetAsync(mine.IDUserGroup));
    }

    // ---- device ---------------------------------------------------------

    [SkippableFact]
    public async Task Device_Add_Creates_Two_Config_Rows_And_Links_Them()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

        Assert.NotNull(d.DeviceConfigSensorID);
        Assert.NotNull(d.DeviceConfigControllerID);

        Assert.NotNull(await _repo.DeviceConfigSensorGetAsync(d.DeviceConfigSensorID));
        Assert.NotNull(await _repo.DeviceConfigControllerGetAsync(d.DeviceConfigControllerID));

        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByDeviceConfigSensorIdAsync(d.DeviceConfigSensorID)).IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByDeviceConfigControllerIdAsync(d.DeviceConfigControllerID)).IDDevice);
    }

    [SkippableFact]
    public async Task DeviceGet_Lookups_Are_Tenant_Scoped()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, d.IDDevice, null, null)).IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, null, d.ApiId, null)).IDDevice);
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetAsync(tenantId, null, null, d.MacAddress)).IDDevice);

        // wrong tenant -> empty Device (IDDevice null)
        Assert.Null((await _repo.DeviceGetAsync(tenantId + 12345, null, d.ApiId, null)).IDDevice);

        // DeviceGetById ignores tenant
        Assert.Equal(d.IDDevice, (await _repo.DeviceGetByIdAsync(d.IDDevice)).IDDevice);

        Assert.Single(await _repo.DevicesGetAsync(tenantId));
        Assert.True(await _repo.DeviceCheckMacAddressAsync(tenantId, d.MacAddress));
        Assert.False(await _repo.DeviceCheckMacAddressAsync(tenantId, "no_" + U()));
    }

    [SkippableFact]
    public async Task DeviceUpdate_Sets_ConfigVersion_To_Payload_Plus_One()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

        d.DeviceName = "renamed";
        d.ConfigVersion = 40;
        await _repo.DeviceUpdateAsync(d);

        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.Equal("renamed", back.DeviceName);
        Assert.Equal(41, back.ConfigVersion);
    }

    [SkippableFact]
    public async Task DeviceConfig_Updates_Persist_And_Bump_Device_ConfigVersion()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);
        int v0 = (await _repo.DeviceGetByIdAsync(d.IDDevice)).ConfigVersion!.Value;

        await _repo.DeviceConfigSensorUpdateAsync(d.IDDevice, new DeviceConfigSensor
        {
            IDDeviceConfigSensor = d.DeviceConfigSensorID, SensorTemp = 1, SensorHumid = 1, SensorCo2 = 1,
        });
        Assert.Equal(v0 + 1, (await _repo.DeviceGetByIdAsync(d.IDDevice)).ConfigVersion);

        await _repo.DeviceConfigControllerUpdateAsync(d.IDDevice, new DeviceConfigController
        {
            IDDeviceConfigController = d.DeviceConfigControllerID, TempLow = 5.5, TempHigh = 30.25, RelayEnabled = true, Relay1 = 2,
        });
        var back = await _repo.DeviceGetByIdAsync(d.IDDevice);
        Assert.Equal(v0 + 2, back.ConfigVersion);

        var ctrl = await _repo.DeviceConfigControllerGetAsync(d.DeviceConfigControllerID);
        Assert.Equal(5.5, ctrl!.TempLow);         // stored as real double (proc truncated to int)
        Assert.Equal(30.25, ctrl.TempHigh);
        Assert.True(ctrl.RelayEnabled);

        var sens = await _repo.DeviceConfigSensorGetAsync(d.DeviceConfigSensorID);
        Assert.Equal(1, sens!.SensorTemp);
    }

    [SkippableFact]
    public async Task DeviceDelete_Removes_Device_And_Its_Config_Rows()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

        await _repo.DeviceDeleteAsync(d.IDDevice, tenantId);

        Assert.Null((await _repo.DeviceGetByIdAsync(d.IDDevice)).IDDevice);
        await using var db = _fx.NewContext();
        Assert.False(await db.DeviceConfigSensors.AnyAsync(c => c.IDDeviceConfigSensor == d.DeviceConfigSensorID));
        Assert.False(await db.DeviceConfigControllers.AnyAsync(c => c.IDDeviceConfigController == d.DeviceConfigControllerID));
    }

    [SkippableFact]
    public async Task DeviceFirmwareLatest_Picks_Newest_By_DateAdded()
    {
        int type = new Random().Next(5000, 9_000_000);
        await using (var db = _fx.NewContext())
        {
            db.DeviceFirmwares.Add(new DeviceFirmwareRow { DeviceTypeID = type, Version = "0.1.0", Url = "u1", DateAdded = DateTime.Now.AddDays(-2) });
            db.DeviceFirmwares.Add(new DeviceFirmwareRow { DeviceTypeID = type, Version = "0.2.0", Url = "u2", DateAdded = DateTime.Now });
            await db.SaveChangesAsync();
        }

        Assert.Equal("0.2.0", (await _repo.DeviceFirmwareLatestGetAsync(type))!.Version);
        Assert.Null(await _repo.DeviceFirmwareLatestGetAsync(-1));
    }

    [SkippableFact]
    public async Task DeviceType_Lists_Return_Seeded_Rows()
    {
        Assert.Contains(await _repo.DeviceTypeGetAsync(), t => t.IDDeviceType == 1);
        Assert.Contains(await _repo.DeviceTypeServiceGetAsync(), t => t.IDDeviceTypeService == 1);
        Assert.Contains(await _repo.DeviceTypeRelayGetAsync(), t => t.IDDeviceTypeRelay == 1);
        Assert.Contains(await _repo.DeviceTypeSensorGetAsync(), t => t.IDDeviceTypeSensor == 1);
    }

    // ---- sensor data --------------------------------------------------

    [SkippableFact]
    public async Task SensorDataPush_Parses_String_Measurements_And_Fills_Missing_Date()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

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
                // no dateCreated -> should become "now"
            });

        await _repo.SensorDataPushAsync(payload);

        await using var db = _fx.NewContext();
        var rows = await db.SensorData.Where(r => r.DeviceID == d.IDDevice).OrderBy(r => r.IDSensorData).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(26.13, rows[0].Temperature);
        Assert.Equal(new DateTime(2026, 8, 29, 9, 50, 0), rows[0].DateCreated);
        Assert.NotNull(rows[1].DateCreated);
        Assert.True(rows[1].DateCreated > DateTime.Now.AddMinutes(-5));
    }

    [SkippableFact]
    public async Task SensorDataGet_Buckets_Rows_Excludes_Null_Co2_And_Writes_Report()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);
        var now = DateTime.Now;

        await using (var db = _fx.NewContext())
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 400, Temperature = 1, DateCreated = now.AddMinutes(-1).AddSeconds(-10) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 401, Temperature = 2, DateCreated = now.AddMinutes(-1) },  // same minute bucket, later
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 402, Temperature = 3, DateCreated = now.AddSeconds(-5) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = null, Temperature = 99, DateCreated = now.AddSeconds(-4) },  // excluded: NULL Co2
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, Co2 = 9000, Temperature = 88, DateCreated = now.AddSeconds(-3) }); // excluded: Co2 >= 8000
            await db.SaveChangesAsync();
        }

        string json = await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 0, 1);
        var arr = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("sensorData");

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(2, arr[0].GetProperty("temperature").GetDouble()); // latest in the older minute bucket
        Assert.Equal(3, arr[1].GetProperty("temperature").GetDouble());

        await using var db2 = _fx.NewContext();
        Assert.True(await db2.SensorDataReports.AnyAsync(r => r.DeviceID == 1000038 && r.SensorData == json));

        Assert.Equal("", await _repo.SensorDataGetAsync(tenantId, d.IDDevice, 10, 7, 0)); // unknown time mode
    }

    [SkippableFact]
    public async Task SensorDataReportGet_Metadata_Then_Full_Row_TenantScoped()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);

        await using (var db = _fx.NewContext())
        {
            db.SensorDataReports.Add(new SensorDataReportRow { DeviceID = d.IDDevice, ReportName = "r1", SensorData = "{\"sensorData\":[]}", DateGenerated = DateTime.Now });
            await db.SaveChangesAsync();
        }

        var meta = await _repo.SensorDataReportGetAsync(tenantId, 0, d.IDDevice, null);
        var one = Assert.Single(meta);
        Assert.Equal("r1", one.ReportName);
        Assert.Null(one.SensorData); // getData == 0 -> metadata only

        var full = await _repo.SensorDataReportGetAsync(tenantId, 1, null, one.IDSensorDataReport);
        Assert.Equal("{\"sensorData\":[]}", Assert.Single(full).SensorData);

        // other tenant sees nothing
        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId + 999, 0, d.IDDevice, null));
        Assert.Empty(await _repo.SensorDataReportGetAsync(tenantId, -1, d.IDDevice, null)); // no matching CASE branch
    }

    [SkippableFact]
    public async Task SensorDataDelete_Removes_Rows_Older_Than_Cutoff()
    {
        var (tenantId, _, _) = await MakeUser();
        var d = await MakeDevice(tenantId);
        var now = DateTime.Now;

        await using (var db = _fx.NewContext())
        {
            db.SensorData.AddRange(
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-10) },
                new SensorDataRow { DeviceID = d.IDDevice!.Value, TenantID = tenantId, DateCreated = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        await _repo.SensorDataDeleteAsync(tenantId, d.IDDevice, 5, 1); // older than 5 days

        await using var db2 = _fx.NewContext();
        var left = await db2.SensorData.Where(r => r.DeviceID == d.IDDevice).ToListAsync();
        Assert.Single(left);
        Assert.True(left[0].DateCreated > now.AddDays(-2));
    }

    // ---- error classification --------------------------------------

    [SkippableFact]
    public async Task ClassifyException_On_Real_Missing_Table_Is_SchemaMissing()
    {
        await using var db = _fx.NewContext();
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
}
