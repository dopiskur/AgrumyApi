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
    /// <summary>
    /// EF Core implementation of <see cref="IRepository"/>. Replaces the Dapper +
    /// <c>CommandType.StoredProcedure</c> <c>SqlRepository</c> (roadmap #42).
    ///
    /// Runs on MySQL/MariaDB (Pomelo) or PostgreSQL (Npgsql), chosen by <c>Database:Provider</c>.
    /// Every method reproduces the effect of the stored procedure it replaced; the reference for
    /// that behaviour is the pre-#42 <c>Schema/SchemaScripts.cs</c> (git history). Persistence goes
    /// through <see cref="Entities"/> row types; results are projected onto the <see cref="api.Models"/>
    /// DTOs so the interface contract is unchanged.
    ///
    /// Roadmap #74: one class, split into partial files mirroring the IRepository facets
    /// (EfRepository.Users.cs, EfRepository.Devices.cs, ...) - this file holds the connection
    /// plumbing and the ISystemRepository members.
    ///
    /// Roadmap #101: constructor-injected <see cref="AgrumyDbContext"/>, registered scoped
    /// (Program.cs) - one context per HTTP request/background-worker tick, not a new one per
    /// method call. Previously every method opened its own via a private static Db() factory,
    /// which meant EF's change tracking never spanned two repo calls in the same request, real
    /// connection-pool churn (open/close per method), and tests had to poke process-wide static
    /// fields (ConnectionStringOverride/ProviderOverride) instead of just constructing an instance.
    /// </summary>
    internal partial class EfRepository(AgrumyDbContext db, IOptions<AgrumySettings> settingsOptions, ILogger<EfRepository> logger) : IRepository
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        // ---- Startup / health -----------------------------------------------------------

        public async Task<bool> TestConnectionAsync()
        {
            return await db.Database.CanConnectAsync();
        }

        public async Task EnsureSchemaAsync()
        {

            // Pre-beta: no real data to preserve across schema changes, so we skip migration
            // history entirely and just create-if-missing from the current model. Empty DB gets
            // every table from AgrumyDbContext as it stands today; shared DB with tables already
            // present is a no-op either way (EnsureCreatedAsync also no-ops if the DB isn't empty).
            // Migrations come back at beta - see roadmap.
            await db.Database.EnsureCreatedAsync();

            // Roadmap #14: PostgreSQL is the "large deployment" tier of the tiered-hybrid decision -
            // MariaDB/MySQL stays a plain relational table (small-deployment tier, no code path here
            // at all). No-op on a MySQL/Pomelo context, and no-op (after logging) on a Postgres
            // context whose server doesn't have the TimescaleDB extension installed.
            await EnsureTimescaleHypertableAsync();

            // #66: EnsureCreatedAsync makes tables, never rows - without the role catalog a fresh
            // install would have every login fall back to the legacy single-role path forever and
            // registration's UserRolesSetAsync would silently assign nothing. Insert-if-missing by
            // name; on a DB migrated via 2026-08-30-rbac-composable-roles.sql these already exist
            // (with proper RoleScopeIDs, which nothing reads for authorization) so this is a no-op.
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
            await SeedBootstrapAdminAsync(db);
        }

        /// <summary>Roadmap #14's tiered-hybrid decision: the deployment-size choice IS the provider
        /// choice, so this runs unconditionally on every Postgres startup rather than needing a
        /// separate opt-in setting - a self-hosted admin who picked Postgres already picked the
        /// "large deployment" tier. `sensorData`'s PK is `IDSensorData` alone (see AgrumyDbContext);
        /// TimescaleDB requires the partitioning column in every unique constraint including the PK,
        /// so converting to a hypertable means widening it to (IDSensorData, DateCreated) first -
        /// IDSensorData keeps working as a lookup key (EfRepository.DeviceUnits.cs's "latest reading
        /// per zone" query), it just stops being unique on its own. Idempotent: skips straight past
        /// an already-converted table, and if the extension itself isn't installed (dev/self-hosted
        /// Postgres without TimescaleDB) this logs a warning and leaves sensorData as an ordinary
        /// table - same as the MariaDB tier, not a startup failure.</summary>
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

            // DO block, not two separate ExecuteSqlRawAsync calls: the PK rename and
            // create_hypertable() must both happen (or neither) exactly once, and the
            // pg_constraint lookup avoids hardcoding EF's "PK_sensorData" naming-convention output.
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
        }

        /// <summary>Roadmap #81/#82: EnsureCreatedAsync makes the deviceUnit/deviceUnitZone tables,
        /// never rows - without the IDDeviceUnit=0/IDDeviceUnitZone=0 sentinel pair, a brand-new
        /// install's very first device registration would violate device.DeviceUnitID's FK (the
        /// Shared Device model defaults DeviceUnitID/DeviceUnitZoneID to 0, not null). Global
        /// (TenantID=null) by design - this is the shared "unassigned" bucket every tenant's
        /// not-yet-zoned devices point at, not one tenant's real data. Zone before... no, Unit
        /// before Zone (Zone's DeviceUnitID FK now points at Unit, opposite of the pre-migration
        /// order - see db/migrations/2026-09-02-deviceunit-zone-containment.sql).</summary>
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

        /// <summary>Roadmap #91: EnsureCreatedAsync makes the four deviceType* tables, never rows -
        /// left empty, every device Add/Edit dropdown in Agrumy.Web is blank and the admin can't
        /// assign a type/service/relay/sensor to anything. Values are the original product's fixed
        /// catalog (db/agrumyDB-final.sql) verbatim, not invented: AgrumyDevice's firmware hardcodes
        /// these same IDs (ControllerController.h's RelayFunctionType enum for deviceTypeRelay,
        /// DeviceController.cpp's serviceType() switch for deviceTypeService), and Agrumy.Web's
        /// DeviceController.Edit switches on the literal deviceType IDs 0/1/2/3 - drifting from this
        /// seed would silently desync the dropdown from what the device/web code actually does with
        /// the ID. Insert-if-missing per table (independent of each other and of the role catalog
        /// above) so a partially-migrated DB that already has some of these rows is a no-op for
        /// that table only.</summary>
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

            await db.SaveChangesAsync();
        }

        /// <summary>Roadmap #91: the other half of "fresh install has nothing to log in with" -
        /// EnsureCreatedAsync/the seeding above populate tables and lookups but never an account,
        /// so a genuinely empty `user` table (only true on the very first run against a brand-new
        /// database - never on a DB that already has accounts, invent.hr included) gets exactly one
        /// row: a Global Admin at TenantID=0 with PwdHash/PwdSalt left NULL. That NULL is
        /// deliberate, not a bug - see UserRow.PwdHash - and is what makes
        /// Agrumy.Web's first-run "set password" screen (BootstrapAdminPendingAsync/
        /// BootstrapAdminSetPasswordAsync below) meaningful: there is nothing to authenticate with
        /// until an operator sets a password through that screen, and once they do the row is
        /// indistinguishable from any other account. No generic "Global User" row is seeded
        /// alongside it - that concept was retired for the default schema (roadmap #91 design note).</summary>
        private static async Task SeedBootstrapAdminAsync(AgrumyDbContext db)
        {
            if (await db.Users.AnyAsync())
            {
                return;
            }

            var admin = new UserRow
            {
                TenantID = 0,
                Email = "admin@agrumy.local",
                Username = "admin",
                PwdHash = null,
                PwdSalt = null,
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
