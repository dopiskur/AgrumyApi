using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace api.Dal
{
    /// EF Core implementation of IRepository, split into partial files mirroring its facets (EfRepository.Users.cs, EfRepository.Devices.cs, ...) - this file holds connection plumbing and the ISystemRepository members.
    internal partial class EfRepository(AgrumyDbContext db, ILogger<EfRepository> logger, IAuditLogRepository auditLogRepository, IRefreshTokenRepository refreshTokenRepository, IControllerDataRepository controllerDataRepository, IDiscoveryRepository discoveryRepository, ITenantRepository tenantRepository, IGatewayRepository gatewayRepository, IServerConfigRepository serverConfigRepository, ICommandRepository commandRepository, IFirmwareRepository firmwareRepository, IUserRepository userRepository, IDeviceRepository deviceRepository, ISimulationRepository simulationRepository, IDeviceUnitRepository deviceUnitRepository) : IRepository
    {

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
            await SeedEventTypeLookupAsync(db);
            await SeedDeviceUnitSentinelsAsync(db);
            await SeedDefaultTenantAsync(db);
            string? bootstrapSecret = await SeedBootstrapAdminAsync(db);
            if (bootstrapSecret != null)
            {
                // The only channel this secret is ever exposed on - never written to the DB in plaintext or returned by any API.
                logger.LogWarning("Bootstrap Global Admin setup secret (required by POST /api/User/BootstrapSetPassword, works once): {BootstrapSecret}", bootstrapSecret);
            }
        }

        /// TimescaleDB requires the partitioning column in every unique constraint including the PK, so this widens sensorData's PK from IDSensorData alone to (IDSensorData, DateCreated) - no-op on MySQL/Pomelo.
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

            // One DO block, not two separate calls - the PK rename and create_hypertable() must both happen (or neither) exactly once.
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
                    -- create_hypertable's first parameter is REGCLASS: an unquoted literal here folds to lowercase and misses this mixed-case table - the embedded double quotes below make it match "sensorData" exactly.
                    PERFORM create_hypertable('"sensorData"', 'DateCreated', migrate_data => true, if_not_exists => true);
                  END IF;
                END $$;
                """;
            await db.Database.ExecuteSqlRawAsync(sql);

            await serverConfigRepository.ApplyRetentionPolicyAsync((await serverConfigRepository.ServerConfigGetAsync(1)).SensorDataRetentionDays);
        }

        /// Seeds the IDDeviceUnit=0/IDDeviceUnitZone=0 sentinel pair, also reserving ID 0 so DeviceUnitAddAsync/DeviceUnitZoneAddAsync's MAX+1 never assigns it to a real Unit/Zone.
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

        /// TenantID=0 is the shared default tenant every bootstrap admin relies on - since tenant.IDTenant stays auto-increment, inserting IDTenant=0 needs raw SQL, and MySQL additionally needs NO_AUTO_VALUE_ON_ZERO or it silently reassigns a literal 0 (confirmed empirically).
        private static async Task SeedDefaultTenantAsync(AgrumyDbContext db)
        {
            if (await db.Tenants.AsNoTracking().AnyAsync(t => t.IDTenant == 0))
            {
                return;
            }

            // Both statements MUST run on the same physical connection (hence the transaction) - otherwise SET SESSION never reaches the connection the INSERT runs on, and MySQL silently reassigns the literal-0 insert to the next auto-increment value.
            await using var tx = await db.Database.BeginTransactionAsync();
            if (db.Database.IsMySql())
            {
                await db.Database.ExecuteSqlRawAsync("SET SESSION sql_mode=(SELECT CONCAT(@@sql_mode, ',NO_AUTO_VALUE_ON_ZERO'))");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO tenant (IDTenant, TenantName) VALUES (0, 'Default')");
            }
            else
            {
                // Npgsql created columns as case-sensitive quoted identifiers - unquoted here would fold to lowercase and miss the real column.
                await db.Database.ExecuteSqlRawAsync("INSERT INTO tenant (\"IDTenant\", \"TenantName\") VALUES (0, 'Default')");
            }
            await tx.CommitAsync();
        }

        /// These IDs must match AgrumyFirmware's ControllerController.h RelayFunctionType enum and DeviceController.cpp, plus Agrumy.Web's DeviceController.Edit - renumbering desyncs the dropdown from what the device actually does.
        private static async Task SeedDeviceTypeLookupsAsync(AgrumyDbContext db)
        {
            if (!await db.DeviceRoles.AnyAsync())
            {
                db.DeviceRoles.AddRange(
                    new DeviceRoleRow { IDDeviceRole = 0, DeviceRoleName = "Basic", SensorEnabled = false, ControllerEnabled = false },
                    new DeviceRoleRow { IDDeviceRole = 1, DeviceRoleName = "Sensor", SensorEnabled = true, ControllerEnabled = false },
                    new DeviceRoleRow { IDDeviceRole = 3, DeviceRoleName = "Sensor+Controller", SensorEnabled = true, ControllerEnabled = true });
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
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Disabled, SensorName = "Disabled", Battery = 1, Temperature = 1, TemperatureSoil = 1, Humidity = 1, Moisture = 1, Light = 1, Co2 = 1, Tvoc = 1, Barometer = 1, WaterPH = 1, WaterTankLevel = 1, RainLevel = 1, Wind = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Dht11, SensorName = "DHT11", Temperature = 1, Humidity = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Dht22, SensorName = "DHT22", Temperature = 1, Humidity = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Bmp180, SensorName = "BMP180", Temperature = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Bmp280, SensorName = "BMP280", Temperature = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Bme280, SensorName = "BME280", Temperature = 1, Humidity = 1, Barometer = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Ccs811, SensorName = "CCS811", Co2 = 1, Tvoc = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Ds18B20, SensorName = "DS18B20", TemperatureSoil = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Bh1750, SensorName = "BH1750", Light = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.Max17048, SensorName = "MAX17048", SensorDescription = "I2C fuel gauge (coulomb counting), address 0x36 - recommended, more precise than a voltage divider", Battery = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.AnalogVoltage, SensorName = "Analog voltage", Battery = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.AnalogMoisture, SensorName = "Analog moisture", Moisture = 1 },
                    new DeviceTypeSensorRow { IDDeviceTypeSensor = SensorTypeIds.AnalogWaterLevel, SensorName = "Analog water tank", WaterTankLevel = 1 });
            }

            // Any Kit string not in this table falls back to the existing, admin-controlled DeviceRole/DeviceControllerEnabled signal - see DeviceFleetGetAsync. VirtualDevice is a software-only kit for fully simulated devices, not a real board.
            if (!await db.DeviceTypes.AnyAsync())
            {
                db.DeviceTypes.AddRange(
                    new DeviceTypeRow { Kit = "KC868-A6", ControllerCapable = true },
                    new DeviceTypeRow { Kit = "ESP32-S3-Relay-6CH", ControllerCapable = true },
                    new DeviceTypeRow { Kit = "VirtualDevice", ControllerCapable = true });
            }

            await db.SaveChangesAsync();
        }

        /// Mirrors DeviceEventType exactly (reflection, not a hand-copied list) so the catalog can never drift from the enum it backs.
        private static async Task SeedEventTypeLookupAsync(AgrumyDbContext db)
        {
            if (!await db.EventTypes.AnyAsync())
            {
                db.EventTypes.AddRange(Enum.GetValues<DeviceEventType>()
                    .Select(t => new EventTypeRow { IDEventType = (int)t, EventTypeName = t.ToString() }));
                await db.SaveChangesAsync();
            }
        }

        /// A genuinely empty user table gets exactly one row: a Global Admin at TenantID=0 with PwdHash/PwdSalt left NULL (see UserRow.PwdHash) for Agrumy.Web's first-run "set password" screen to activate - returns the plaintext setup secret once, or null if no row was created.
        private static async Task<string?> SeedBootstrapAdminAsync(AgrumyDbContext db)
        {
            if (await db.Users.AnyAsync())
            {
                return null;
            }

            // Without this, BootstrapSetPassword's only gate was rate limiting - anyone could claim the Global Admin account first.
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

        public DbFailureKind ClassifyException(Exception ex) => DbExceptionClassifier.Classify(ex);
    }
}
