using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Agrumy.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    IDDevice = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantID = table.Column<int>(type: "integer", nullable: true),
                    DeviceTypeID = table.Column<int>(type: "integer", nullable: true),
                    DeviceUnitID = table.Column<int>(type: "integer", nullable: true),
                    DeviceUnitZoneID = table.Column<int>(type: "integer", nullable: true),
                    DeviceConfigSensorID = table.Column<int>(type: "integer", nullable: true),
                    DeviceConfigControllerID = table.Column<int>(type: "integer", nullable: true),
                    DeviceTypeServiceID = table.Column<int>(type: "integer", nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MacAddress = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ApiId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ServicePoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ServicePublicKey = table.Column<string>(type: "text", nullable: true),
                    SleepSeconds = table.Column<int>(type: "integer", nullable: true),
                    SleepDeepEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    DeviceSensorEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    DeviceControllerEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    BatteryEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: true),
                    Debug = table.Column<bool>(type: "boolean", nullable: true),
                    Reboot = table.Column<bool>(type: "boolean", nullable: true),
                    Reset = table.Column<bool>(type: "boolean", nullable: true),
                    FirmwareUpdate = table.Column<bool>(type: "boolean", nullable: true),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DateModified = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device", x => x.IDDevice);
                });

            migrationBuilder.CreateTable(
                name: "deviceConfigController",
                columns: table => new
                {
                    IDDeviceConfigController = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TempLow = table.Column<double>(type: "double precision", nullable: true),
                    TempHigh = table.Column<double>(type: "double precision", nullable: true),
                    HumidLow = table.Column<double>(type: "double precision", nullable: true),
                    HumidHigh = table.Column<double>(type: "double precision", nullable: true),
                    MoistLow = table.Column<double>(type: "double precision", nullable: true),
                    MoistHigh = table.Column<double>(type: "double precision", nullable: true),
                    LightLow = table.Column<double>(type: "double precision", nullable: true),
                    LightHigh = table.Column<double>(type: "double precision", nullable: true),
                    WaterLow = table.Column<double>(type: "double precision", nullable: true),
                    WaterHigh = table.Column<double>(type: "double precision", nullable: true),
                    RelayEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    Relay1 = table.Column<int>(type: "integer", nullable: true),
                    Relay2 = table.Column<int>(type: "integer", nullable: true),
                    Relay3 = table.Column<int>(type: "integer", nullable: true),
                    Relay4 = table.Column<int>(type: "integer", nullable: true),
                    Relay5 = table.Column<int>(type: "integer", nullable: true),
                    Relay6 = table.Column<int>(type: "integer", nullable: true),
                    Relay7 = table.Column<int>(type: "integer", nullable: true),
                    Relay8 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceConfigController", x => x.IDDeviceConfigController);
                });

            migrationBuilder.CreateTable(
                name: "deviceConfigSensor",
                columns: table => new
                {
                    IDDeviceConfigSensor = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SensorBattery = table.Column<int>(type: "integer", nullable: true),
                    SensorTemp = table.Column<int>(type: "integer", nullable: true),
                    SensorTempSoil = table.Column<int>(type: "integer", nullable: true),
                    SensorHumid = table.Column<int>(type: "integer", nullable: true),
                    SensorMoist = table.Column<int>(type: "integer", nullable: true),
                    SensorLight = table.Column<int>(type: "integer", nullable: true),
                    SensorCo2 = table.Column<int>(type: "integer", nullable: true),
                    SensorTvoc = table.Column<int>(type: "integer", nullable: true),
                    SensorBarometer = table.Column<int>(type: "integer", nullable: true),
                    SensorPH = table.Column<int>(type: "integer", nullable: true),
                    SensorRainLevel = table.Column<int>(type: "integer", nullable: true),
                    SensorWaterLevel = table.Column<int>(type: "integer", nullable: true),
                    SensorWind = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceConfigSensor", x => x.IDDeviceConfigSensor);
                });

            migrationBuilder.CreateTable(
                name: "deviceFirmware",
                columns: table => new
                {
                    IDDeviceFirmware = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceTypeID = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Url = table.Column<string>(type: "text", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceFirmware", x => x.IDDeviceFirmware);
                });

            migrationBuilder.CreateTable(
                name: "deviceType",
                columns: table => new
                {
                    IDDeviceType = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceTypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SensorEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    ControllerEnabled = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceType", x => x.IDDeviceType);
                });

            migrationBuilder.CreateTable(
                name: "deviceTypeRelay",
                columns: table => new
                {
                    IDDeviceTypeRelay = table.Column<int>(type: "integer", nullable: false),
                    RelayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeRelay", x => x.IDDeviceTypeRelay);
                });

            migrationBuilder.CreateTable(
                name: "deviceTypeSensor",
                columns: table => new
                {
                    IDDeviceTypeSensor = table.Column<int>(type: "integer", nullable: false),
                    SensorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SensorDescription = table.Column<string>(type: "text", nullable: true),
                    Battery = table.Column<int>(type: "integer", nullable: true),
                    Temperature = table.Column<int>(type: "integer", nullable: true),
                    TemperatureSoil = table.Column<int>(type: "integer", nullable: true),
                    Humidity = table.Column<int>(type: "integer", nullable: true),
                    Moisture = table.Column<int>(type: "integer", nullable: true),
                    Light = table.Column<int>(type: "integer", nullable: true),
                    Co2 = table.Column<int>(type: "integer", nullable: true),
                    Tvoc = table.Column<int>(type: "integer", nullable: true),
                    Barometer = table.Column<int>(type: "integer", nullable: true),
                    WaterPH = table.Column<int>(type: "integer", nullable: true),
                    WaterTankLevel = table.Column<int>(type: "integer", nullable: true),
                    RainLevel = table.Column<int>(type: "integer", nullable: true),
                    Wind = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeSensor", x => x.IDDeviceTypeSensor);
                });

            migrationBuilder.CreateTable(
                name: "deviceTypeService",
                columns: table => new
                {
                    IDDeviceTypeService = table.Column<int>(type: "integer", nullable: false),
                    ServiceType = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeService", x => x.IDDeviceTypeService);
                });

            migrationBuilder.CreateTable(
                name: "deviceUnit",
                columns: table => new
                {
                    IDDeviceUnit = table.Column<int>(type: "integer", nullable: false),
                    DeviceUnitZoneID = table.Column<int>(type: "integer", nullable: true),
                    DeviceUnitName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ZoneEnabled = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceUnit", x => x.IDDeviceUnit);
                });

            migrationBuilder.CreateTable(
                name: "deviceUnitZone",
                columns: table => new
                {
                    IDDeviceUnitZone = table.Column<int>(type: "integer", nullable: false),
                    DeviceUnitZoneName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceUnitZone", x => x.IDDeviceUnitZone);
                });

            migrationBuilder.CreateTable(
                name: "eventDevice",
                columns: table => new
                {
                    IDEventDevice = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceID = table.Column<int>(type: "integer", nullable: false),
                    EventID = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventDevice", x => x.IDEventDevice);
                });

            migrationBuilder.CreateTable(
                name: "eventService",
                columns: table => new
                {
                    IDEventService = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceID = table.Column<int>(type: "integer", nullable: false),
                    EventID = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventService", x => x.IDEventService);
                });

            migrationBuilder.CreateTable(
                name: "sensorData",
                columns: table => new
                {
                    IDSensorData = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantID = table.Column<int>(type: "integer", nullable: false),
                    DeviceID = table.Column<int>(type: "integer", nullable: false),
                    DeviceUnitID = table.Column<int>(type: "integer", nullable: false),
                    DeviceUnitZoneID = table.Column<int>(type: "integer", nullable: false),
                    Battery = table.Column<int>(type: "integer", nullable: true),
                    Temperature = table.Column<double>(type: "double precision", nullable: true),
                    SoilTemperature = table.Column<double>(type: "double precision", nullable: true),
                    Humidity = table.Column<double>(type: "double precision", nullable: true),
                    Moisture = table.Column<int>(type: "integer", nullable: true),
                    Light = table.Column<int>(type: "integer", nullable: true),
                    Co2 = table.Column<int>(type: "integer", nullable: true),
                    Tvoc = table.Column<int>(type: "integer", nullable: true),
                    Barometer = table.Column<double>(type: "double precision", nullable: true),
                    LiquidPH = table.Column<double>(type: "double precision", nullable: true),
                    RainLevel = table.Column<int>(type: "integer", nullable: true),
                    WaterLevel = table.Column<int>(type: "integer", nullable: true),
                    Wind = table.Column<int>(type: "integer", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensorData", x => x.IDSensorData);
                });

            migrationBuilder.CreateTable(
                name: "sensorDataReport",
                columns: table => new
                {
                    IDSensorDataReport = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deviceID = table.Column<int>(type: "integer", nullable: true),
                    ReportName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DateGenerated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    sensorData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensorDataReport", x => x.IDSensorDataReport);
                });

            migrationBuilder.CreateTable(
                name: "serverConfig",
                columns: table => new
                {
                    IDServerConfig = table.Column<int>(type: "integer", nullable: false),
                    ServerConfigName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConfigKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    JWTKey = table.Column<string>(type: "text", nullable: true),
                    PortHTTP = table.Column<int>(type: "integer", nullable: true),
                    PortHTTPS = table.Column<int>(type: "integer", nullable: true),
                    serverConfigcol = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serverConfig", x => x.IDServerConfig);
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    IDTenant = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.IDTenant);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    IDUser = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantID = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PwdHash = table.Column<string>(type: "text", nullable: false),
                    PwdSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DevicePin = table.Column<int>(type: "integer", nullable: true),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    UserGroupID = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DateModified = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.IDUser);
                });

            migrationBuilder.CreateTable(
                name: "userGroup",
                columns: table => new
                {
                    IDUserGroup = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GroupName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserRoleID = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userGroup", x => x.IDUserGroup);
                });

            migrationBuilder.CreateTable(
                name: "userRole",
                columns: table => new
                {
                    IDUserRole = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleName = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RoleScopeID = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userRole", x => x.IDUserRole);
                });

            migrationBuilder.CreateTable(
                name: "userRoleScope",
                columns: table => new
                {
                    IDRoleScope = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleScopeName = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userRoleScope", x => x.IDRoleScope);
                });

            migrationBuilder.CreateIndex(
                name: "ApiID_UNIQUE",
                table: "device",
                column: "ApiId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sensorData_device_tenant_date",
                table: "sensorData",
                columns: new[] { "DeviceID", "TenantID", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "Name_UNIQUE",
                table: "tenant",
                column: "TenantName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "email_UNIQUE",
                table: "user",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Username_UNIQUE",
                table: "user",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device");

            migrationBuilder.DropTable(
                name: "deviceConfigController");

            migrationBuilder.DropTable(
                name: "deviceConfigSensor");

            migrationBuilder.DropTable(
                name: "deviceFirmware");

            migrationBuilder.DropTable(
                name: "deviceType");

            migrationBuilder.DropTable(
                name: "deviceTypeRelay");

            migrationBuilder.DropTable(
                name: "deviceTypeSensor");

            migrationBuilder.DropTable(
                name: "deviceTypeService");

            migrationBuilder.DropTable(
                name: "deviceUnit");

            migrationBuilder.DropTable(
                name: "deviceUnitZone");

            migrationBuilder.DropTable(
                name: "eventDevice");

            migrationBuilder.DropTable(
                name: "eventService");

            migrationBuilder.DropTable(
                name: "sensorData");

            migrationBuilder.DropTable(
                name: "sensorDataReport");

            migrationBuilder.DropTable(
                name: "serverConfig");

            migrationBuilder.DropTable(
                name: "tenant");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "userGroup");

            migrationBuilder.DropTable(
                name: "userRole");

            migrationBuilder.DropTable(
                name: "userRoleScope");
        }
    }
}
