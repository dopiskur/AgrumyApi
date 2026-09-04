using api.Dal.Entities;
using api.Dal.Interface;
using api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace api.Dal
{
    /// <summary>EF Core implementation of <see cref="IRepository"/>, running on MySQL/MariaDB
    /// (Pomelo) or PostgreSQL (Npgsql) per <c>Database:Provider</c>; split into partial files
    /// mirroring the IRepository facets (EfRepository.Users.cs, EfRepository.Devices.cs, ...) -
    /// this file holds the connection plumbing and the ISystemRepository members.</summary>
    internal partial class EfRepository(AgrumyDbContext db, IOptions<AgrumySettings> settingsOptions, ILogger<EfRepository> logger, ICache cache) : IRepository
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        // ---- Startup / health -----------------------------------------------------------

        public async Task<bool> TestConnectionAsync()
        {
            return await db.Database.CanConnectAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            await db.Database.EnsureCreatedAsync();

            await EnsureTimescaleHypertableAsync();

            var existingNames = await db.UserRoles.AsNoTracking()
                .Where(r => r.RoleName != null).Select(r => r.RoleName!).ToListAsync();
            var missing = RoleNames.All.Except(existingNames).ToList();
            if (missing.Count > 0)
            {
                db.UserRoles.AddRange(missing.Select(name => new UserRoleRow { RoleName = name }));
                await db.SaveChangesAsync();
            }

            await SeedDeviceTypeLookupsAsync(db);
            await SeedDeviceUnitSentinelsAsync(db);
            string? bootstrapSecret = await SeedBootstrapAdminAsync(db);
            if (bootstrapSecret != null)
            {
                // The only channel this secret is ever exposed on - deliberately not written to the
                // database in plaintext or returned by any API. Whoever deployed this instance reads
                // it from here (journalctl/console) to complete first-run setup (roadmap #179).
                logger.LogWarning("Bootstrap Global Admin setup secret (required by POST /api/User/BootstrapSetPassword, works once): {BootstrapSecret}", bootstrapSecret);
            }
        }

        /// <summary>TimescaleDB requires the partitioning column in every unique constraint
        /// including the PK, so converting sensorData to a hypertable means widening its PK from
        /// IDSensorData alone to (IDSensorData, DateCreated) first. No-op on MySQL/Pomelo, and
        /// logs-and-skips if the TimescaleDB extension isn't installed.</summary>
        private async Task EnsureTimescaleHypertableAsync()
        {
            if (!db.Database.IsNpgsql())
            {
                return;
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS timescaledb;");
            }
            catch (PostgresException ex)
            {
                logger.LogWarning(ex,
                    "TimescaleDB extension unavailable on this PostgreSQL server; sensorData stays a plain table.");
                return;
            }

            // One DO block, not two separate calls - the PK rename and create_hypertable() must
            // both happen (or neither) exactly once.
            const string sql = """
                DO $$
                DECLARE
                  pk_name text;
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensorData'
                  ) THEN
                    SELECT conname INTO pk_name FROM pg_constraint
                      WHERE conrelid = '"sensorData"'::regclass AND contype = 'p';
                    IF pk_name IS NOT NULL THEN
                      EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', 'sensorData', pk_name);
                    END IF;
                    ALTER TABLE "sensorData" ADD PRIMARY KEY ("IDSensorData", "DateCreated");
                    -- create_hypertable's first parameter is REGCLASS: an unquoted-looking literal
                    -- here gets folded to lowercase by the implicit text->regclass cast (same
                    -- identifier-normalization rule as bare SQL), missing this mixed-case table -
                    -- the embedded double quotes below are what make it match "sensorData" exactly.
                    PERFORM create_hypertable('"sensorData"', 'DateCreated', migrate_data => true, if_not_exists => true);
                  END IF;
                END $$;
                """;
            await db.Database.ExecuteSqlRawAsync(sql);

            await ApplyRetentionPolicyAsync((await ServerConfigGetAsync(1)).SensorDataRetentionDays);
        }

        /// <summary>PostgreSQL/TimescaleDB side of sensorData retention - MariaDB's equivalent is
        /// SensorDataRetentionBackgroundService's daily purge. add_retention_policy's interval can
        /// only be changed by removing the old policy and adding a new one, so this runs
        /// unconditionally on every save; null/0 removes any existing policy rather than adding one.
        /// No-op on MySQL/Pomelo.</summary>
        private async Task ApplyRetentionPolicyAsync(int? retentionDays)
        {
            if (!db.Database.IsNpgsql())
            {
                return;
            }

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """SELECT remove_retention_policy('"sensorData"'::regclass, if_exists => true);""");

                if (retentionDays is > 0)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""SELECT add_retention_policy('"sensorData"'::regclass, INTERVAL '1 day' * {retentionDays.Value}, if_not_exists => true);""");
                }
            }
            catch (PostgresException ex)
            {
                logger.LogWarning(ex,
                    "Could not apply sensorData retention policy; automatic PostgreSQL retention stays inactive.");
            }
        }

        /// <summary>Seeds the IDDeviceUnit=0/IDDeviceUnitZone=0 sentinel pair - without it, a
        /// brand-new install's first device registration violates device.DeviceUnitID's FK, since
        /// the Shared Device model defaults DeviceUnitID/DeviceUnitZoneID to 0, not null. Global
        /// (TenantID=null): the shared "unassigned" bucket every tenant's not-yet-zoned devices
        /// point at.</summary>
        private static async Task SeedDeviceUnitSentinelsAsync(AgrumyDbContext db)
        {
            if (!await db.DeviceUnits.AnyAsync())
            {
                db.DeviceUnits.Add(new DeviceUnitRow { IDDeviceUnit = 0, TenantID = null, DeviceUnitName = "Default" });
                await db.SaveChangesAsync();
            }
            if (!await db.DeviceUnitZones.AnyAsync())
            {
                db.DeviceUnitZones.Add(new DeviceUnitZoneRow { IDDeviceUnitZone = 0, TenantID = null, DeviceUnitID = 0, DeviceUnitZoneName = "Disabled" });
                await db.SaveChangesAsync();
            }
        }

        /// <summary>These IDs must match AgrumyFirmware's ControllerController.h RelayFunctionType
        /// enum and DeviceController.cpp serviceType() switch, and Agrumy.Web's
        /// DeviceController.Edit - renumbering desyncs the dropdown from what the device/web code
        /// actually does with the ID.</summary>
        private static async Task SeedDeviceTypeLookupsAsync(AgrumyDbContext db)
        {
            if (!await db.DeviceTypes.AnyAsync())
            {
                db.DeviceTypes.AddRange(
                    new DeviceTypeRow { IDDeviceType = 0, DeviceTypeName = "Basic", SensorEnabled = false, ControllerEnabled = false },
                    new DeviceTypeRow { IDDeviceType = 1, DeviceTypeName = "Sensor", SensorEnabled = true, ControllerEnabled = false },
                    new DeviceTypeRow { IDDeviceType = 3, DeviceTypeName = "Sensor+Controller", SensorEnabled = true, ControllerEnabled = true });
            }

            if (!await db.DeviceTypeServices.AnyAsync())
            {
                db.DeviceTypeServices.AddRange(
                    new DeviceTypeServiceRow { IDDeviceTypeService = 0, ServiceType = "HTTP" },
                    new DeviceTypeServiceRow { IDDeviceTypeService = 1, ServiceType = "HTTPS" },
                    new DeviceTypeServiceRow { IDDeviceTypeService = 2, ServiceType = "MQTT" });
            }

            if (!await db.DeviceTypeRelays.AnyAsync())
            {
                db.DeviceTypeRelays.AddRange(
                    new DeviceTypeRelayRow { IDDeviceTypeRelay = 0, RelayName = "Disabled" },
                    new DeviceTypeRelayRow { IDDeviceTypeRelay = 1, RelayName = "Ventilation" },
                    new DeviceTypeRelayRow { IDDeviceTypeRelay = 2, RelayName = "Light" },
                    new DeviceTypeRelayRow { IDDeviceTypeRelay = 3, RelayName = "Heating" },
                    new DeviceTypeRelayRow { IDDeviceTypeRelay = 4, RelayName = "Water pump" });
            }

            if (!await db.DeviceTypeSensors.AnyAsync())
            {
                db.DeviceTypeSensors.AddRange(
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 0, SensorName = "Disabled", Battery = 1, Temperature = 1, TemperatureSoil = 1, Humidity = 1, Moisture = 1, Light = 1, Co2 = 1, Tvoc = 1, Barometer = 1, WaterPH = 1, WaterTankLevel = 1, RainLevel = 1, Wind = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1001, SensorName = "DHT11", Temperature = 1, Humidity = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1002, SensorName = "DHT22", Temperature = 1, Humidity = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1003, SensorName = "BMP180", Temperature = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1004, SensorName = "BMP280", Temperature = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1005, SensorName = "BME280", Temperature = 1, Humidity = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1006, SensorName = "CCS811", Co2 = 1, Tvoc = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1007, SensorName = "DS18B20", TemperatureSoil = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 1008, SensorName = "BH1750", Light = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 2001, SensorName = "Analog voltage", Battery = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = 2002, SensorName = "Analog moisture", Moisture = 1 });
            }

            // Any Kit string not in this table falls back to the existing, admin-controlled
            // DeviceType/DeviceControllerEnabled signal - see DeviceFleetGetAsync.
            if (!await db.DeviceTypeKits.AnyAsync())
            {
                db.DeviceTypeKits.AddRange(
                    new DeviceTypeKitRow { Kit = "KC868-A6", ControllerCapable = true },
                    new DeviceTypeKitRow { Kit = "ESP32-S3-Relay-6CH", ControllerCapable = true });
            }

            await db.SaveChangesAsync();
        }

        /// <summary>A genuinely empty user table gets exactly one row: a Global Admin at
        /// TenantID=0 with PwdHash/PwdSalt left NULL on purpose - see UserRow.PwdHash - so
        /// Agrumy.Web's first-run "set password" screen (BootstrapAdminSetPasswordAsync below) has
        /// something to activate. Returns the plaintext one-time setup secret (only hashed copy is
        /// persisted) so the caller can surface it once, or null if no row was created.</summary>
        private static async Task<string?> SeedBootstrapAdminAsync(AgrumyDbContext db)
        {
            if (await db.Users.AnyAsync())
            {
                return null;
            }

            // Roadmap #179: without this, BootstrapSetPassword's only gate was rate limiting - a
            // random anonymous visitor who requests it before the real admin does takes over the
            // Global Admin account. 24 random bytes, base64url so it round-trips cleanly through a
            // request body/URL/log line with no escaping surprises.
            string setupSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            string secretSalt = AuthenticationProvider.GetSalt();

            var admin = new UserRow
            {
                TenantID = 0,
                Email = "admin@agrumy.local",
                Username = "admin",
                PwdHash = null,
                PwdSalt = null,
                BootstrapSecretHash = AuthenticationProvider.GetHash(setupSecret, secretSalt),
                BootstrapSecretSalt = secretSalt,
                FirstName = "Global",
                LastName = "Admin",
                Enabled = true,
                EmailVerified = true, // bootstrap account - nobody to send/click an activation link
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            int globalAdminRoleId = await db.UserRoles.AsNoTracking()
                .Where(r => r.RoleName == RoleNames.GlobalAdmin)
                .Select(r => r.IDUserRole)
                .FirstAsync(); // guaranteed present - the role-catalog seed above always runs first

            db.UserUserRoles.Add(new UserUserRoleRow { UserID = admin.IDUser, UserRoleID = globalAdminRoleId });
            await db.SaveChangesAsync();

            return setupSecret;
        }

        public DbFailureKind ClassifyException(Exception ex)
        {
            Exception inner = ex is DbUpdateException due && due.InnerException != null ? due.InnerException : ex;

            // MySql error numbers: 1146 ER_NO_SUCH_TABLE, 1051 ER_BAD_TABLE_ERROR, 1305 SP_DOES_NOT_EXIST;
            // 1216/1217/1451/1452 FK violation, 1062 duplicate key, 1213 deadlock, 1205 lock-wait timeout.
            if (inner is MySqlException mysqlEx)
            {
                switch (mysqlEx.Number)
                {
                    case 1146:
                    case 1051:
                    case 1305:
                        return DbFailureKind.SchemaMissing;
                    case 1216:
                    case 1217:
                    case 1451:
                    case 1452:
                    case 1062:
                        return DbFailureKind.ConstraintViolation;
                    case 1213:
                    case 1205:
                        return DbFailureKind.Contention;
                }
                // Any other MySql error still reached the server but failed - treat as a connection-level failure.
                return DbFailureKind.ConnectionFailure;
            }

            // PostgreSQL SQLSTATE: 42P01 undefined_table, 42703 undefined_column, 3F000 invalid_schema_name;
            // 23503 FK violation, 23505 unique violation, 23514 check violation; 40P01 deadlock,
            // 40001 serialization failure, 55P03 lock not available.
            if (inner is PostgresException pgEx)
            {
                switch (pgEx.SqlState)
                {
                    case "42P01":
                    case "42703":
                    case "3F000":
                        return DbFailureKind.SchemaMissing;
                    case "23503":
                    case "23505":
                    case "23514":
                        return DbFailureKind.ConstraintViolation;
                    case "40P01":
                    case "40001":
                    case "55P03":
                        return DbFailureKind.Contention;
                }
                return DbFailureKind.ConnectionFailure;
            }

            // MySQL text fallback for a missing table when the exception type isn't MySqlException.
            // (PostgreSQL's "relation ... does not exist" is covered by the 42P01 SqlState above -
            // a bare "does not exist" match would also swallow unrelated errors like a missing
            // trigger definer.)
            if (DbErrorResponse.Mentions(ex, "doesn't exist") ||
                DbErrorResponse.Mentions(ex, "Unknown table"))
            {
                return DbFailureKind.SchemaMissing;
            }

            // Genuine transport-level failures still mean "can't reach the DB, retry later" (503).
            if (ex is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException ||
                inner is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException)
            {
                return DbFailureKind.ConnectionFailure;
            }

            // Anything else escaping an action (e.g. a not-found ArgumentException from the DAL) is a
            // server-side bug, not a database outage - let it surface as 500, not a misleading 503.
            return DbFailureKind.Unknown;
        }
    }
}
