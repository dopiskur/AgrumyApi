using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agrumy.Api.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    IDDevice = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantID = table.Column<int>(type: "int", nullable: true),
                    DeviceTypeID = table.Column<int>(type: "int", nullable: true),
                    DeviceUnitID = table.Column<int>(type: "int", nullable: true),
                    DeviceUnitZoneID = table.Column<int>(type: "int", nullable: true),
                    DeviceConfigSensorID = table.Column<int>(type: "int", nullable: true),
                    DeviceConfigControllerID = table.Column<int>(type: "int", nullable: true),
                    DeviceTypeServiceID = table.Column<int>(type: "int", nullable: true),
                    DeviceName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MacAddress = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServicePoint = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServicePublicKey = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SleepSeconds = table.Column<int>(type: "int", nullable: true),
                    SleepDeepEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    DeviceSensorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    DeviceControllerEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    BatteryEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Debug = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Reboot = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Reset = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    FirmwareUpdate = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ConfigVersion = table.Column<int>(type: "int", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DateModified = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device", x => x.IDDevice);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceConfigController",
                columns: table => new
                {
                    IDDeviceConfigController = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TempLow = table.Column<double>(type: "double", nullable: true),
                    TempHigh = table.Column<double>(type: "double", nullable: true),
                    HumidLow = table.Column<double>(type: "double", nullable: true),
                    HumidHigh = table.Column<double>(type: "double", nullable: true),
                    MoistLow = table.Column<double>(type: "double", nullable: true),
                    MoistHigh = table.Column<double>(type: "double", nullable: true),
                    LightLow = table.Column<double>(type: "double", nullable: true),
                    LightHigh = table.Column<double>(type: "double", nullable: true),
                    WaterLow = table.Column<double>(type: "double", nullable: true),
                    WaterHigh = table.Column<double>(type: "double", nullable: true),
                    RelayEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Relay1 = table.Column<int>(type: "int", nullable: true),
                    Relay2 = table.Column<int>(type: "int", nullable: true),
                    Relay3 = table.Column<int>(type: "int", nullable: true),
                    Relay4 = table.Column<int>(type: "int", nullable: true),
                    Relay5 = table.Column<int>(type: "int", nullable: true),
                    Relay6 = table.Column<int>(type: "int", nullable: true),
                    Relay7 = table.Column<int>(type: "int", nullable: true),
                    Relay8 = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceConfigController", x => x.IDDeviceConfigController);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceConfigSensor",
                columns: table => new
                {
                    IDDeviceConfigSensor = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SensorBattery = table.Column<int>(type: "int", nullable: true),
                    SensorTemp = table.Column<int>(type: "int", nullable: true),
                    SensorTempSoil = table.Column<int>(type: "int", nullable: true),
                    SensorHumid = table.Column<int>(type: "int", nullable: true),
                    SensorMoist = table.Column<int>(type: "int", nullable: true),
                    SensorLight = table.Column<int>(type: "int", nullable: true),
                    SensorCo2 = table.Column<int>(type: "int", nullable: true),
                    SensorTvoc = table.Column<int>(type: "int", nullable: true),
                    SensorBarometer = table.Column<int>(type: "int", nullable: true),
                    SensorPH = table.Column<int>(type: "int", nullable: true),
                    SensorRainLevel = table.Column<int>(type: "int", nullable: true),
                    SensorWaterLevel = table.Column<int>(type: "int", nullable: true),
                    SensorWind = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceConfigSensor", x => x.IDDeviceConfigSensor);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceFirmware",
                columns: table => new
                {
                    IDDeviceFirmware = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeviceTypeID = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateAdded = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceFirmware", x => x.IDDeviceFirmware);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceType",
                columns: table => new
                {
                    IDDeviceType = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeviceTypeName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SensorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ControllerEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceType", x => x.IDDeviceType);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceTypeRelay",
                columns: table => new
                {
                    IDDeviceTypeRelay = table.Column<int>(type: "int", nullable: false),
                    RelayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeRelay", x => x.IDDeviceTypeRelay);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceTypeSensor",
                columns: table => new
                {
                    IDDeviceTypeSensor = table.Column<int>(type: "int", nullable: false),
                    SensorName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SensorDescription = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Battery = table.Column<int>(type: "int", nullable: true),
                    Temperature = table.Column<int>(type: "int", nullable: true),
                    TemperatureSoil = table.Column<int>(type: "int", nullable: true),
                    Humidity = table.Column<int>(type: "int", nullable: true),
                    Moisture = table.Column<int>(type: "int", nullable: true),
                    Light = table.Column<int>(type: "int", nullable: true),
                    Co2 = table.Column<int>(type: "int", nullable: true),
                    Tvoc = table.Column<int>(type: "int", nullable: true),
                    Barometer = table.Column<int>(type: "int", nullable: true),
                    WaterPH = table.Column<int>(type: "int", nullable: true),
                    WaterTankLevel = table.Column<int>(type: "int", nullable: true),
                    RainLevel = table.Column<int>(type: "int", nullable: true),
                    Wind = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeSensor", x => x.IDDeviceTypeSensor);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceTypeService",
                columns: table => new
                {
                    IDDeviceTypeService = table.Column<int>(type: "int", nullable: false),
                    ServiceType = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceTypeService", x => x.IDDeviceTypeService);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceUnit",
                columns: table => new
                {
                    IDDeviceUnit = table.Column<int>(type: "int", nullable: false),
                    DeviceUnitZoneID = table.Column<int>(type: "int", nullable: true),
                    DeviceUnitName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ZoneEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceUnit", x => x.IDDeviceUnit);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deviceUnitZone",
                columns: table => new
                {
                    IDDeviceUnitZone = table.Column<int>(type: "int", nullable: false),
                    DeviceUnitZoneName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deviceUnitZone", x => x.IDDeviceUnitZone);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "eventDevice",
                columns: table => new
                {
                    IDEventDevice = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeviceID = table.Column<int>(type: "int", nullable: false),
                    EventID = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Message = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventDevice", x => x.IDEventDevice);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "eventService",
                columns: table => new
                {
                    IDEventService = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ServiceID = table.Column<int>(type: "int", nullable: false),
                    EventID = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Message = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventService", x => x.IDEventService);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sensorData",
                columns: table => new
                {
                    IDSensorData = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantID = table.Column<int>(type: "int", nullable: false),
                    DeviceID = table.Column<int>(type: "int", nullable: false),
                    DeviceUnitID = table.Column<int>(type: "int", nullable: false),
                    DeviceUnitZoneID = table.Column<int>(type: "int", nullable: false),
                    Battery = table.Column<int>(type: "int", nullable: true),
                    Temperature = table.Column<double>(type: "double", nullable: true),
                    SoilTemperature = table.Column<double>(type: "double", nullable: true),
                    Humidity = table.Column<double>(type: "double", nullable: true),
                    Moisture = table.Column<int>(type: "int", nullable: true),
                    Light = table.Column<int>(type: "int", nullable: true),
                    Co2 = table.Column<int>(type: "int", nullable: true),
                    Tvoc = table.Column<int>(type: "int", nullable: true),
                    Barometer = table.Column<double>(type: "double", nullable: true),
                    LiquidPH = table.Column<double>(type: "double", nullable: true),
                    RainLevel = table.Column<int>(type: "int", nullable: true),
                    WaterLevel = table.Column<int>(type: "int", nullable: true),
                    Wind = table.Column<int>(type: "int", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensorData", x => x.IDSensorData);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sensorDataReport",
                columns: table => new
                {
                    IDSensorDataReport = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    deviceID = table.Column<int>(type: "int", nullable: true),
                    ReportName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateGenerated = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    sensorData = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensorDataReport", x => x.IDSensorDataReport);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "serverConfig",
                columns: table => new
                {
                    IDServerConfig = table.Column<int>(type: "int", nullable: false),
                    ServerConfigName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JWTKey = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PortHTTP = table.Column<int>(type: "int", nullable: true),
                    PortHTTPS = table.Column<int>(type: "int", nullable: true),
                    serverConfigcol = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serverConfig", x => x.IDServerConfig);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    IDTenant = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.IDTenant);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    IDUser = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenantID = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PwdHash = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PwdSalt = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DevicePin = table.Column<int>(type: "int", nullable: true),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserGroupID = table.Column<int>(type: "int", nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DateModified = table.Column<DateTime>(type: "datetime(6)", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.IDUser);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "userGroup",
                columns: table => new
                {
                    IDUserGroup = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GroupName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserRoleID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userGroup", x => x.IDUserGroup);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "userRole",
                columns: table => new
                {
                    IDUserRole = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleName = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoleScopeID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userRole", x => x.IDUserRole);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "userRoleScope",
                columns: table => new
                {
                    IDRoleScope = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleScopeName = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userRoleScope", x => x.IDRoleScope);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
