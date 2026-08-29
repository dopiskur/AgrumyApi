using api.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>
    /// EF Core context for the Agrumy database. Replaces the Dapper + stored-procedure
    /// <c>SqlRepository</c> / <c>Schema.SchemaScripts</c> pair (roadmap #42).
    ///
    /// Mapped against the legacy MySQL/MariaDB schema: camelCase table names, <c>IDXxx</c> primary
    /// keys, a mix of AUTO_INCREMENT and manually-assigned ids. Relationships are intentionally not
    /// configured - EfRepository does every join explicitly in LINQ - so the baseline migration
    /// creates tables without the legacy NO-ACTION foreign keys. That is fine for a fresh database
    /// and irrelevant to the existing shared one (EnsureSchemaAsync never migrates it).
    /// </summary>
    internal class AgrumyDbContext : DbContext
    {
        public AgrumyDbContext(DbContextOptions<AgrumyDbContext> options) : base(options) { }

        public DbSet<TenantRow> Tenants => Set<TenantRow>();
        public DbSet<UserRow> Users => Set<UserRow>();
        public DbSet<UserGroupRow> UserGroups => Set<UserGroupRow>();
        public DbSet<UserRoleRow> UserRoles => Set<UserRoleRow>();
        public DbSet<UserRoleScopeRow> UserRoleScopes => Set<UserRoleScopeRow>();
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

        public DbSet<SensorDataRow> SensorData => Set<SensorDataRow>();
        public DbSet<SensorDataReportRow> SensorDataReports => Set<SensorDataReportRow>();
        public DbSet<EventDeviceRow> EventDevices => Set<EventDeviceRow>();
        public DbSet<EventServiceRow> EventServices => Set<EventServiceRow>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<TenantRow>(e =>
            {
                e.ToTable("tenant");
                e.HasKey(x => x.IDTenant);
                e.Property(x => x.IDTenant).ValueGeneratedOnAdd();
                e.Property(x => x.TenantName).HasMaxLength(100).IsRequired();
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.TenantName).IsUnique().HasDatabaseName("Name_UNIQUE");
            });

            b.Entity<UserRoleScopeRow>(e =>
            {
                e.ToTable("userRoleScope");
                e.HasKey(x => x.IDRoleScope);
                e.Property(x => x.IDRoleScope).ValueGeneratedOnAdd();
                e.Property(x => x.RoleScopeName).HasMaxLength(45);
            });

            b.Entity<UserRoleRow>(e =>
            {
                e.ToTable("userRole");
                e.HasKey(x => x.IDUserRole);
                e.Property(x => x.IDUserRole).ValueGeneratedOnAdd();
                e.Property(x => x.RoleName).HasMaxLength(45);
            });

            b.Entity<UserGroupRow>(e =>
            {
                e.ToTable("userGroup");
                e.HasKey(x => x.IDUserGroup);
                e.Property(x => x.IDUserGroup).ValueGeneratedOnAdd();
                e.Property(x => x.GroupName).HasMaxLength(128);
            });

            b.Entity<UserRow>(e =>
            {
                e.ToTable("user");
                e.HasKey(x => x.IDUser);
                e.Property(x => x.IDUser).ValueGeneratedOnAdd();
                e.Property(x => x.Email).HasMaxLength(100).IsRequired();
                e.Property(x => x.Username).HasMaxLength(100);
                e.Property(x => x.PwdHash).HasColumnType("text").IsRequired();
                e.Property(x => x.PwdSalt).HasMaxLength(128).IsRequired();
                e.Property(x => x.FirstName).HasMaxLength(100);
                e.Property(x => x.LastName).HasMaxLength(100);
                e.Property(x => x.Phone).HasMaxLength(15);
                e.Property(x => x.Enabled).HasColumnType("tinyint(1)");
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.DateModified).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("email_UNIQUE");
                e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("Username_UNIQUE");
            });

            b.Entity<ServerConfigRow>(e =>
            {
                e.ToTable("serverConfig");
                e.HasKey(x => x.IDServerConfig);
                e.Property(x => x.IDServerConfig).ValueGeneratedNever();
                e.Property(x => x.ServerConfigName).HasMaxLength(100);
                e.Property(x => x.ConfigKey).HasMaxLength(128).IsRequired();
                e.Property(x => x.JWTKey).HasColumnType("text");
                e.Property(x => x.ServerConfigCol).HasColumnName("serverConfigcol").HasMaxLength(45);
            });

            b.Entity<DeviceUnitZoneRow>(e =>
            {
                e.ToTable("deviceUnitZone");
                e.HasKey(x => x.IDDeviceUnitZone);
                e.Property(x => x.IDDeviceUnitZone).ValueGeneratedNever();
                e.Property(x => x.DeviceUnitZoneName).HasMaxLength(120);
            });

            b.Entity<DeviceUnitRow>(e =>
            {
                e.ToTable("deviceUnit");
                e.HasKey(x => x.IDDeviceUnit);
                e.Property(x => x.IDDeviceUnit).ValueGeneratedNever();
                e.Property(x => x.DeviceUnitName).HasMaxLength(100);
                e.Property(x => x.ZoneEnabled).HasColumnType("bit(1)");
            });

            b.Entity<DeviceTypeRow>(e =>
            {
                e.ToTable("deviceType");
                e.HasKey(x => x.IDDeviceType);
                e.Property(x => x.IDDeviceType).ValueGeneratedOnAdd();
                e.Property(x => x.DeviceTypeName).HasMaxLength(100);
                e.Property(x => x.SensorEnabled).HasColumnType("tinyint(1)");
                e.Property(x => x.ControllerEnabled).HasColumnType("tinyint(1)");
            });

            b.Entity<DeviceTypeServiceRow>(e =>
            {
                e.ToTable("deviceTypeService");
                e.HasKey(x => x.IDDeviceTypeService);
                e.Property(x => x.IDDeviceTypeService).ValueGeneratedNever();
                e.Property(x => x.ServiceType).HasMaxLength(5);
            });

            b.Entity<DeviceTypeRelayRow>(e =>
            {
                e.ToTable("deviceTypeRelay");
                e.HasKey(x => x.IDDeviceTypeRelay);
                e.Property(x => x.IDDeviceTypeRelay).ValueGeneratedNever();
                e.Property(x => x.RelayName).HasMaxLength(128);
            });

            b.Entity<DeviceTypeSensorRow>(e =>
            {
                e.ToTable("deviceTypeSensor");
                e.HasKey(x => x.IDDeviceTypeSensor);
                e.Property(x => x.IDDeviceTypeSensor).ValueGeneratedNever();
                e.Property(x => x.SensorName).HasMaxLength(128);
                e.Property(x => x.SensorDescription).HasColumnType("text");
            });

            b.Entity<DeviceConfigControllerRow>(e =>
            {
                e.ToTable("deviceConfigController");
                e.HasKey(x => x.IDDeviceConfigController);
                e.Property(x => x.IDDeviceConfigController).ValueGeneratedOnAdd();
                e.Property(x => x.RelayEnabled).HasColumnType("tinyint(1)");
            });

            b.Entity<DeviceConfigSensorRow>(e =>
            {
                e.ToTable("deviceConfigSensor");
                e.HasKey(x => x.IDDeviceConfigSensor);
                e.Property(x => x.IDDeviceConfigSensor).ValueGeneratedOnAdd();
            });

            b.Entity<DeviceRow>(e =>
            {
                e.ToTable("device");
                e.HasKey(x => x.IDDevice);
                e.Property(x => x.IDDevice).ValueGeneratedOnAdd();
                e.Property(x => x.DeviceName).HasMaxLength(128);
                e.Property(x => x.MacAddress).HasMaxLength(12);
                e.Property(x => x.ApiId).HasMaxLength(128).IsRequired();
                e.Property(x => x.ApiKey).HasMaxLength(128).IsRequired();
                e.Property(x => x.ServicePoint).HasMaxLength(200);
                e.Property(x => x.ServicePublicKey).HasColumnType("text");
                e.Property(x => x.SleepDeepEnabled).HasColumnType("tinyint(1)");
                e.Property(x => x.DeviceSensorEnabled).HasColumnType("tinyint(1)");
                e.Property(x => x.DeviceControllerEnabled).HasColumnType("tinyint(1)");
                e.Property(x => x.BatteryEnabled).HasColumnType("tinyint(1)");
                e.Property(x => x.Enabled).HasColumnType("tinyint(1)");
                e.Property(x => x.Debug).HasColumnType("tinyint(1)");
                e.Property(x => x.Reboot).HasColumnType("tinyint(1)");
                e.Property(x => x.Reset).HasColumnType("tinyint(1)");
                e.Property(x => x.FirmwareUpdate).HasColumnType("tinyint(1)");
                e.Property(x => x.DateCreated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.DateModified).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.HasIndex(x => x.ApiId).IsUnique().HasDatabaseName("ApiID_UNIQUE");
            });

            b.Entity<DeviceFirmwareRow>(e =>
            {
                e.ToTable("deviceFirmware");
                e.HasKey(x => x.IDDeviceFirmware);
                e.Property(x => x.IDDeviceFirmware).ValueGeneratedOnAdd();
                e.Property(x => x.Version).HasMaxLength(20);
                e.Property(x => x.Url).HasColumnType("text");
                e.Property(x => x.DateAdded).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            b.Entity<SensorDataRow>(e =>
            {
                e.ToTable("sensorData");
                e.HasKey(x => x.IDSensorData);
                e.Property(x => x.IDSensorData).ValueGeneratedOnAdd();
                // Legacy columns Battery / Moisture / WaterLevel are tinyint(1); the DTO and these
                // rows expose them as int. Pomelo won't map int -> tinyint(1), so a fresh database
                // gets plain int here - harmless, and reads of the existing tinyint(1) columns still
                // materialise fine.
                e.HasIndex(x => new { x.DeviceID, x.TenantID, x.DateCreated })
                 .HasDatabaseName("ix_sensorData_device_tenant_date");
            });

            b.Entity<SensorDataReportRow>(e =>
            {
                e.ToTable("sensorDataReport");
                e.HasKey(x => x.IDSensorDataReport);
                e.Property(x => x.IDSensorDataReport).ValueGeneratedOnAdd();
                e.Property(x => x.DeviceID).HasColumnName("deviceID");
                e.Property(x => x.ReportName).HasMaxLength(128);
                e.Property(x => x.DateGenerated).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.SensorData).HasColumnName("sensorData").HasColumnType("longtext");
            });

            b.Entity<EventDeviceRow>(e =>
            {
                e.ToTable("eventDevice");
                e.HasKey(x => x.IDEventDevice);
                e.Property(x => x.IDEventDevice).ValueGeneratedOnAdd();
                e.Property(x => x.Message).HasColumnType("text");
            });

            b.Entity<EventServiceRow>(e =>
            {
                e.ToTable("eventService");
                e.HasKey(x => x.IDEventService);
                e.Property(x => x.IDEventService).ValueGeneratedOnAdd();
                e.Property(x => x.Message).HasColumnType("text");
            });
        }
    }
}
