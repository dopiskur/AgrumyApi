using api.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>
    /// EF Core context for the Agrumy database, replacing the Dapper + stored-procedure SqlRepository (roadmap #42).
    ///
    /// Provider-neutral (Phase 2): MySQL/MariaDB (Pomelo) or PostgreSQL (Npgsql), chosen at runtime by
    /// <c>Database:Provider</c>. No vendor-specific <c>HasColumnType</c> - EF maps each CLR type per
    /// provider; only portable <c>HasMaxLength</c> and <c>CURRENT_TIMESTAMP</c> defaults are used.
    ///
    /// Mapped against the legacy schema (camelCase tables, <c>IDXxx</c> keys, mixed id strategies).
    /// Relationships are intentionally not configured - EfRepository does every join in LINQ - so the
    /// baseline migration creates tables without the legacy NO-ACTION FKs (fine for a fresh database,
    /// irrelevant to one that already has tables).
    /// </summary>
    public class AgrumyDbContext : DbContext
    {
        public AgrumyDbContext(DbContextOptions<AgrumyDbContext> options) : base(options) { }

        public DbSet<TenantRow> Tenants => Set<TenantRow>();
        public DbSet<UserRow> Users => Set<UserRow>();
        public DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();
        public DbSet<UserGroupRow> UserGroups => Set<UserGroupRow>();
        public DbSet<UserRoleRow> UserRoles => Set<UserRoleRow>();
        public DbSet<UserRoleScopeRow> UserRoleScopes => Set<UserRoleScopeRow>();
        public DbSet<UserUserRoleRow> UserUserRoles => Set<UserUserRoleRow>();
        public DbSet<ServerConfigRow> ServerConfigs => Set<ServerConfigRow>();

        public DbSet<DeviceRow> Devices => Set<DeviceRow>();
        public DbSet<DeviceUnitRow> DeviceUnits => Set<DeviceUnitRow>();
        public DbSet<DeviceUnitZoneRow> DeviceUnitZones => Set<DeviceUnitZoneRow>();
        public DbSet<DeviceTypeRow> DeviceTypes => Set<DeviceTypeRow>();
        public DbSet<DeviceTypeServiceRow> DeviceTypeServices => Set<DeviceTypeServiceRow>();
        public DbSet<DeviceTypeRelayRow> DeviceTypeRelays => Set<DeviceTypeRelayRow>();
        public DbSet<DeviceTypeSensorRow> DeviceTypeSensors => Set<DeviceTypeSensorRow>();
        public DbSet<DeviceConfigSensorRow> DeviceConfigSensors => Set<DeviceConfigSensorRow>();
        public DbSet<DeviceConfigControllerRow> DeviceConfigControllers => Set<DeviceConfigControllerRow>();
        public DbSet<DeviceFirmwareRow> DeviceFirmwares => Set<DeviceFirmwareRow>();
        public DbSet<DeviceDiagnosticRow> DeviceDiagnostics => Set<DeviceDiagnosticRow>();

        public DbSet<SensorDataRow> SensorData => Set<SensorDataRow>();
        public DbSet<SensorDataReportRow> SensorDataReports => Set<SensorDataReportRow>();
        public DbSet<EventDeviceRow> EventDevices => Set<EventDeviceRow>();
        public DbSet<EventServiceRow> EventServices => Set<EventServiceRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantRow>(e =>
            {
                e.ToTable("tenant");
                e.HasKey(x => x.IDTenant);
                e.Property(x => x.IDTenant).ValueGeneratedOnAdd();
                e.Property(x => x.TenantName).HasMaxLength(100).IsRequired();
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.TenantName).IsUnique().HasDatabaseName("Name_UNIQUE");
            });

            modelBuilder.Entity<UserRoleScopeRow>(e =>
            {
                e.ToTable("userRoleScope");
                e.HasKey(x => x.IDRoleScope);
                e.Property(x => x.IDRoleScope).ValueGeneratedOnAdd();
                e.Property(x => x.RoleScopeName).HasMaxLength(45);
            });

            modelBuilder.Entity<UserRoleRow>(e =>
            {
                e.ToTable("userRole");
                e.HasKey(x => x.IDUserRole);
                e.Property(x => x.IDUserRole).ValueGeneratedOnAdd();
                e.Property(x => x.RoleName).HasMaxLength(45);
                e.HasOne<UserRoleScopeRow>().WithMany().HasForeignKey(x => x.RoleScopeID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<UserUserRoleRow>(e =>
            {
                e.ToTable("userUserRole");
                e.HasKey(x => new { x.UserID, x.UserRoleID }); // composite - a user cannot hold the same role twice
                e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.Cascade); // deleting a user drops their role rows
                e.HasOne<UserRoleRow>().WithMany().HasForeignKey(x => x.UserRoleID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<UserGroupRow>(e =>
            {
                e.ToTable("userGroup");
                e.HasKey(x => x.IDUserGroup);
                e.Property(x => x.IDUserGroup).ValueGeneratedOnAdd();
                e.Property(x => x.GroupName).HasMaxLength(128);
                e.HasOne<UserRoleRow>().WithMany().HasForeignKey(x => x.UserRoleID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<UserRow>(e =>
            {
                e.ToTable("user");
                e.HasKey(x => x.IDUser);
                e.Property(x => x.IDUser).ValueGeneratedOnAdd();
                e.Property(x => x.Email).HasMaxLength(100).IsRequired();
                e.Property(x => x.Username).HasMaxLength(100);
                // Roadmap #91: nullable now - see UserRow.PwdHash for why.
                e.Property(x => x.PwdSalt).HasMaxLength(128);
                e.Property(x => x.FirstName).HasMaxLength(100);
                e.Property(x => x.LastName).HasMaxLength(100);
                e.Property(x => x.Phone).HasMaxLength(15);
                e.Property(x => x.DevicePin).HasMaxLength(8); // 6 today; the firmware buffer (char devicePin[8]) caps what a device can echo back at 7 anyway
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.DateModified).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.ActivationTokenHash).HasMaxLength(64); // SHA-256 hex, same shape as userRefreshToken.TokenHash
                e.Property(x => x.TimeZone).HasMaxLength(64); // longest IANA ids are ~30 chars; 64 leaves headroom
                e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("email_UNIQUE");
                e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("Username_UNIQUE");
                e.HasIndex(x => x.ActivationTokenHash).IsUnique().HasDatabaseName("ActivationTokenHash_UNIQUE");
                // user.TenantID has no FK in the legacy schema - only UserGroupID does.
                e.HasOne<UserGroupRow>().WithMany().HasForeignKey(x => x.UserGroupID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<RefreshTokenRow>(e =>
            {
                e.ToTable("userRefreshToken");
                e.HasKey(x => x.IDRefreshToken);
                e.Property(x => x.IDRefreshToken).ValueGeneratedOnAdd();
                e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
                e.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
                e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("TokenHash_UNIQUE");
                e.HasIndex(x => x.UserID).HasDatabaseName("ix_userRefreshToken_userID");
                e.HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ServerConfigRow>(e =>
            {
                e.ToTable("serverConfig");
                e.HasKey(x => x.IDServerConfig);
                e.Property(x => x.IDServerConfig).ValueGeneratedNever();
                e.Property(x => x.ServerConfigName).HasMaxLength(100);
                e.Property(x => x.ConfigKey).HasMaxLength(128).IsRequired();
                e.Property(x => x.ServerConfigCol).HasColumnName("serverConfigcol").HasMaxLength(45);
                e.Property(x => x.ScheduleTimeZone).HasMaxLength(64); // same cap as user.TimeZone (roadmap #71)
            });

            modelBuilder.Entity<DeviceUnitRow>(e =>
            {
                e.ToTable("deviceUnit");
                e.HasKey(x => x.IDDeviceUnit);
                e.Property(x => x.IDDeviceUnit).ValueGeneratedNever();
                e.Property(x => x.DeviceUnitName).HasMaxLength(100);
            });

            // Roadmap #81/#82: real containment FK (Zone -> Unit), replacing the removed backwards
            // deviceUnit.DeviceUnitZoneID pointer - see db/migrations/2026-09-02-deviceunit-zone-containment.sql.
            modelBuilder.Entity<DeviceUnitZoneRow>(e =>
            {
                e.ToTable("deviceUnitZone");
                e.HasKey(x => x.IDDeviceUnitZone);
                e.Property(x => x.IDDeviceUnitZone).ValueGeneratedNever();
                e.Property(x => x.DeviceUnitZoneName).HasMaxLength(120);
                e.HasOne<DeviceUnitRow>().WithMany().HasForeignKey(x => x.DeviceUnitID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceTypeRow>(e =>
            {
                e.ToTable("deviceType");
                e.HasKey(x => x.IDDeviceType);
                // Roadmap #91: fixed catalog, not an admin-creatable list (like its three
                // deviceType* siblings below) - Agrumy.Web.Controllers.View.DeviceController.Edit
                // switches on the literal IDs 0/1/2/3, so the seed must control them exactly the
                // same way deviceTypeRelay/Service/Sensor already do.
                e.Property(x => x.IDDeviceType).ValueGeneratedNever();
                e.Property(x => x.DeviceTypeName).HasMaxLength(100);
            });

            modelBuilder.Entity<DeviceTypeServiceRow>(e =>
            {
                e.ToTable("deviceTypeService");
                e.HasKey(x => x.IDDeviceTypeService);
                e.Property(x => x.IDDeviceTypeService).ValueGeneratedNever();
                e.Property(x => x.ServiceType).HasMaxLength(5);
            });

            modelBuilder.Entity<DeviceTypeRelayRow>(e =>
            {
                e.ToTable("deviceTypeRelay");
                e.HasKey(x => x.IDDeviceTypeRelay);
                e.Property(x => x.IDDeviceTypeRelay).ValueGeneratedNever();
                e.Property(x => x.RelayName).HasMaxLength(128);
            });

            modelBuilder.Entity<DeviceTypeSensorRow>(e =>
            {
                e.ToTable("deviceTypeSensor");
                e.HasKey(x => x.IDDeviceTypeSensor);
                e.Property(x => x.IDDeviceTypeSensor).ValueGeneratedNever();
                e.Property(x => x.SensorName).HasMaxLength(128);
            });

            modelBuilder.Entity<DeviceConfigControllerRow>(e =>
            {
                e.ToTable("deviceConfigController");
                e.HasKey(x => x.IDDeviceConfigController);
                e.Property(x => x.IDDeviceConfigController).ValueGeneratedOnAdd();
                // Relay1-8 each reference deviceTypeRelay (legacy fk_deviceConfigController_relayN).
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay1).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay2).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay3).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay4).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay5).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay6).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay7).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRelayRow>().WithMany().HasForeignKey(x => x.Relay8).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceConfigSensorRow>(e =>
            {
                e.ToTable("deviceConfigSensor");
                e.HasKey(x => x.IDDeviceConfigSensor);
                e.Property(x => x.IDDeviceConfigSensor).ValueGeneratedOnAdd();
                // Every Sensor* column references deviceTypeSensor (legacy fk_deviceConfigSensor_deviceTypeSensor_*).
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorBattery).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorTemp).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorTempSoil).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorHumid).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorMoist).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorLight).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorCo2).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorTvoc).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorBarometer).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorPH).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorRainLevel).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorWaterLevel).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeSensorRow>().WithMany().HasForeignKey(x => x.SensorWind).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceRow>(e =>
            {
                e.ToTable("device");
                e.HasKey(x => x.IDDevice);
                e.Property(x => x.IDDevice).ValueGeneratedOnAdd();
                e.Property(x => x.DeviceName).HasMaxLength(128);
                e.Property(x => x.MacAddress).HasMaxLength(12);
                e.Property(x => x.ApiId).HasMaxLength(128).IsRequired();
                e.Property(x => x.ApiKey).HasMaxLength(128).IsRequired();
                e.Property(x => x.ServicePoint).HasMaxLength(200);
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.DateModified).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.ApiId).IsUnique().HasDatabaseName("ApiID_UNIQUE");
                // Roadmap #102: composite, not a bare MacAddress unique - a physical device is
                // legitimately resold across tenants (old tenant keeps its historical row, new
                // tenant registers a "new" row with the same MAC), but a duplicate register
                // request within the SAME tenant (double click, firmware retry) must not create
                // two rows for one device. NULL MacAddress/TenantID rows never collide under this
                // index on either MySQL or PostgreSQL, so pre-registration rows are unaffected.
                e.HasIndex(x => new { x.MacAddress, x.TenantID }).IsUnique().HasDatabaseName("MacAddress_TenantID_UNIQUE");
                // Legacy device FKs (fk_device_*). DeviceUnitZoneID has no FK on device.
                e.HasOne<DeviceConfigControllerRow>().WithMany().HasForeignKey(x => x.DeviceConfigControllerID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceConfigSensorRow>().WithMany().HasForeignKey(x => x.DeviceConfigSensorID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeRow>().WithMany().HasForeignKey(x => x.DeviceTypeID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceTypeServiceRow>().WithMany().HasForeignKey(x => x.DeviceTypeServiceID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<TenantRow>().WithMany().HasForeignKey(x => x.TenantID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceUnitRow>().WithMany().HasForeignKey(x => x.DeviceUnitID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceDiagnosticRow>(e =>
            {
                e.ToTable("deviceDiagnostic");
                // DeviceID is the PK (1:1 with device, roadmap #7) - deliberately NOT ValueGeneratedOnAdd.
                e.HasKey(x => x.DeviceID);
                e.Property(x => x.DeviceID).ValueGeneratedNever();
                e.Property(x => x.FirmwareVersion).HasMaxLength(20); // same cap as deviceFirmware.Version
                e.HasOne<DeviceRow>().WithMany().HasForeignKey(x => x.DeviceID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceFirmwareRow>(e =>
            {
                e.ToTable("deviceFirmware");
                e.HasKey(x => x.IDDeviceFirmware);
                e.Property(x => x.IDDeviceFirmware).ValueGeneratedOnAdd();
                e.Property(x => x.Version).HasMaxLength(20);
                e.Property(x => x.DateAdded).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            modelBuilder.Entity<SensorDataRow>(e =>
            {
                e.ToTable("sensorData");
                e.HasKey(x => x.IDSensorData);
                e.Property(x => x.IDSensorData).ValueGeneratedOnAdd();
                // Legacy Battery/Moisture/WaterLevel are tinyint(1); the DTO exposes them as int, so a fresh DB uses int (old tinyint(1) columns still read fine).
                e.HasIndex(x => new { x.DeviceID, x.TenantID, x.DateCreated })
                 .HasDatabaseName("ix_sensorData_device_tenant_date");
                // Legacy fk_sensorData_* (no FK on sensorData.TenantID).
                e.HasOne<DeviceRow>().WithMany().HasForeignKey(x => x.DeviceID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceUnitRow>().WithMany().HasForeignKey(x => x.DeviceUnitID).OnDelete(DeleteBehavior.NoAction);
                e.HasOne<DeviceUnitZoneRow>().WithMany().HasForeignKey(x => x.DeviceUnitZoneID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<SensorDataReportRow>(e =>
            {
                e.ToTable("sensorDataReport");
                e.HasKey(x => x.IDSensorDataReport);
                e.Property(x => x.IDSensorDataReport).ValueGeneratedOnAdd();
                e.Property(x => x.DeviceID).HasColumnName("deviceID");
                e.Property(x => x.ReportName).HasMaxLength(128);
                e.Property(x => x.DateGenerated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.SensorData).HasColumnName("sensorData");
                e.HasOne<DeviceRow>().WithMany().HasForeignKey(x => x.DeviceID).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<EventDeviceRow>(e =>
            {
                e.ToTable("eventDevice");
                e.HasKey(x => x.IDEventDevice);
                e.Property(x => x.IDEventDevice).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<EventServiceRow>(e =>
            {
                e.ToTable("eventService");
                e.HasKey(x => x.IDEventService);
                e.Property(x => x.IDEventService).ValueGeneratedOnAdd();
            });
        }
    }
}
