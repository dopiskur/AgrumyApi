using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agrumy.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_userRole_RoleScopeID",
                table: "userRole",
                column: "RoleScopeID");

            migrationBuilder.CreateIndex(
                name: "IX_userGroup_UserRoleID",
                table: "userGroup",
                column: "UserRoleID");

            migrationBuilder.CreateIndex(
                name: "IX_user_UserGroupID",
                table: "user",
                column: "UserGroupID");

            migrationBuilder.CreateIndex(
                name: "IX_sensorDataReport_deviceID",
                table: "sensorDataReport",
                column: "deviceID");

            migrationBuilder.CreateIndex(
                name: "IX_sensorData_DeviceUnitID",
                table: "sensorData",
                column: "DeviceUnitID");

            migrationBuilder.CreateIndex(
                name: "IX_sensorData_DeviceUnitZoneID",
                table: "sensorData",
                column: "DeviceUnitZoneID");

            migrationBuilder.CreateIndex(
                name: "IX_deviceUnit_DeviceUnitZoneID",
                table: "deviceUnit",
                column: "DeviceUnitZoneID");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorBarometer",
                table: "deviceConfigSensor",
                column: "SensorBarometer");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorBattery",
                table: "deviceConfigSensor",
                column: "SensorBattery");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorCo2",
                table: "deviceConfigSensor",
                column: "SensorCo2");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorHumid",
                table: "deviceConfigSensor",
                column: "SensorHumid");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorLight",
                table: "deviceConfigSensor",
                column: "SensorLight");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorMoist",
                table: "deviceConfigSensor",
                column: "SensorMoist");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorPH",
                table: "deviceConfigSensor",
                column: "SensorPH");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorRainLevel",
                table: "deviceConfigSensor",
                column: "SensorRainLevel");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorTemp",
                table: "deviceConfigSensor",
                column: "SensorTemp");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorTempSoil",
                table: "deviceConfigSensor",
                column: "SensorTempSoil");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorTvoc",
                table: "deviceConfigSensor",
                column: "SensorTvoc");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorWaterLevel",
                table: "deviceConfigSensor",
                column: "SensorWaterLevel");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigSensor_SensorWind",
                table: "deviceConfigSensor",
                column: "SensorWind");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay1",
                table: "deviceConfigController",
                column: "Relay1");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay2",
                table: "deviceConfigController",
                column: "Relay2");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay3",
                table: "deviceConfigController",
                column: "Relay3");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay4",
                table: "deviceConfigController",
                column: "Relay4");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay5",
                table: "deviceConfigController",
                column: "Relay5");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay6",
                table: "deviceConfigController",
                column: "Relay6");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay7",
                table: "deviceConfigController",
                column: "Relay7");

            migrationBuilder.CreateIndex(
                name: "IX_deviceConfigController_Relay8",
                table: "deviceConfigController",
                column: "Relay8");

            migrationBuilder.CreateIndex(
                name: "IX_device_DeviceConfigControllerID",
                table: "device",
                column: "DeviceConfigControllerID");

            migrationBuilder.CreateIndex(
                name: "IX_device_DeviceConfigSensorID",
                table: "device",
                column: "DeviceConfigSensorID");

            migrationBuilder.CreateIndex(
                name: "IX_device_DeviceTypeID",
                table: "device",
                column: "DeviceTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_device_DeviceTypeServiceID",
                table: "device",
                column: "DeviceTypeServiceID");

            migrationBuilder.CreateIndex(
                name: "IX_device_DeviceUnitID",
                table: "device",
                column: "DeviceUnitID");

            migrationBuilder.CreateIndex(
                name: "IX_device_TenantID",
                table: "device",
                column: "TenantID");

            migrationBuilder.AddForeignKey(
                name: "FK_device_deviceConfigController_DeviceConfigControllerID",
                table: "device",
                column: "DeviceConfigControllerID",
                principalTable: "deviceConfigController",
                principalColumn: "IDDeviceConfigController");

            migrationBuilder.AddForeignKey(
                name: "FK_device_deviceConfigSensor_DeviceConfigSensorID",
                table: "device",
                column: "DeviceConfigSensorID",
                principalTable: "deviceConfigSensor",
                principalColumn: "IDDeviceConfigSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_device_deviceTypeService_DeviceTypeServiceID",
                table: "device",
                column: "DeviceTypeServiceID",
                principalTable: "deviceTypeService",
                principalColumn: "IDDeviceTypeService");

            migrationBuilder.AddForeignKey(
                name: "FK_device_deviceType_DeviceTypeID",
                table: "device",
                column: "DeviceTypeID",
                principalTable: "deviceType",
                principalColumn: "IDDeviceType");

            migrationBuilder.AddForeignKey(
                name: "FK_device_deviceUnit_DeviceUnitID",
                table: "device",
                column: "DeviceUnitID",
                principalTable: "deviceUnit",
                principalColumn: "IDDeviceUnit");

            migrationBuilder.AddForeignKey(
                name: "FK_device_tenant_TenantID",
                table: "device",
                column: "TenantID",
                principalTable: "tenant",
                principalColumn: "IDTenant");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay1",
                table: "deviceConfigController",
                column: "Relay1",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay2",
                table: "deviceConfigController",
                column: "Relay2",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay3",
                table: "deviceConfigController",
                column: "Relay3",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay4",
                table: "deviceConfigController",
                column: "Relay4",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay5",
                table: "deviceConfigController",
                column: "Relay5",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay6",
                table: "deviceConfigController",
                column: "Relay6",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay7",
                table: "deviceConfigController",
                column: "Relay7",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay8",
                table: "deviceConfigController",
                column: "Relay8",
                principalTable: "deviceTypeRelay",
                principalColumn: "IDDeviceTypeRelay");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorBarometer",
                table: "deviceConfigSensor",
                column: "SensorBarometer",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorBattery",
                table: "deviceConfigSensor",
                column: "SensorBattery",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorCo2",
                table: "deviceConfigSensor",
                column: "SensorCo2",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorHumid",
                table: "deviceConfigSensor",
                column: "SensorHumid",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorLight",
                table: "deviceConfigSensor",
                column: "SensorLight",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorMoist",
                table: "deviceConfigSensor",
                column: "SensorMoist",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorPH",
                table: "deviceConfigSensor",
                column: "SensorPH",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorRainLevel",
                table: "deviceConfigSensor",
                column: "SensorRainLevel",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTemp",
                table: "deviceConfigSensor",
                column: "SensorTemp",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTempSoil",
                table: "deviceConfigSensor",
                column: "SensorTempSoil",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTvoc",
                table: "deviceConfigSensor",
                column: "SensorTvoc",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorWaterLevel",
                table: "deviceConfigSensor",
                column: "SensorWaterLevel",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorWind",
                table: "deviceConfigSensor",
                column: "SensorWind",
                principalTable: "deviceTypeSensor",
                principalColumn: "IDDeviceTypeSensor");

            migrationBuilder.AddForeignKey(
                name: "FK_deviceUnit_deviceUnitZone_DeviceUnitZoneID",
                table: "deviceUnit",
                column: "DeviceUnitZoneID",
                principalTable: "deviceUnitZone",
                principalColumn: "IDDeviceUnitZone");

            migrationBuilder.AddForeignKey(
                name: "FK_sensorData_deviceUnitZone_DeviceUnitZoneID",
                table: "sensorData",
                column: "DeviceUnitZoneID",
                principalTable: "deviceUnitZone",
                principalColumn: "IDDeviceUnitZone");

            migrationBuilder.AddForeignKey(
                name: "FK_sensorData_deviceUnit_DeviceUnitID",
                table: "sensorData",
                column: "DeviceUnitID",
                principalTable: "deviceUnit",
                principalColumn: "IDDeviceUnit");

            migrationBuilder.AddForeignKey(
                name: "FK_sensorData_device_DeviceID",
                table: "sensorData",
                column: "DeviceID",
                principalTable: "device",
                principalColumn: "IDDevice");

            migrationBuilder.AddForeignKey(
                name: "FK_sensorDataReport_device_deviceID",
                table: "sensorDataReport",
                column: "deviceID",
                principalTable: "device",
                principalColumn: "IDDevice");

            migrationBuilder.AddForeignKey(
                name: "FK_user_userGroup_UserGroupID",
                table: "user",
                column: "UserGroupID",
                principalTable: "userGroup",
                principalColumn: "IDUserGroup");

            migrationBuilder.AddForeignKey(
                name: "FK_userGroup_userRole_UserRoleID",
                table: "userGroup",
                column: "UserRoleID",
                principalTable: "userRole",
                principalColumn: "IDUserRole");

            migrationBuilder.AddForeignKey(
                name: "FK_userRole_userRoleScope_RoleScopeID",
                table: "userRole",
                column: "RoleScopeID",
                principalTable: "userRoleScope",
                principalColumn: "IDRoleScope");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_device_deviceConfigController_DeviceConfigControllerID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_device_deviceConfigSensor_DeviceConfigSensorID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_device_deviceTypeService_DeviceTypeServiceID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_device_deviceType_DeviceTypeID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_device_deviceUnit_DeviceUnitID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_device_tenant_TenantID",
                table: "device");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay1",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay2",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay3",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay4",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay5",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay6",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay7",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigController_deviceTypeRelay_Relay8",
                table: "deviceConfigController");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorBarometer",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorBattery",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorCo2",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorHumid",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorLight",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorMoist",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorPH",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorRainLevel",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTemp",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTempSoil",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorTvoc",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorWaterLevel",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceConfigSensor_deviceTypeSensor_SensorWind",
                table: "deviceConfigSensor");

            migrationBuilder.DropForeignKey(
                name: "FK_deviceUnit_deviceUnitZone_DeviceUnitZoneID",
                table: "deviceUnit");

            migrationBuilder.DropForeignKey(
                name: "FK_sensorData_deviceUnitZone_DeviceUnitZoneID",
                table: "sensorData");

            migrationBuilder.DropForeignKey(
                name: "FK_sensorData_deviceUnit_DeviceUnitID",
                table: "sensorData");

            migrationBuilder.DropForeignKey(
                name: "FK_sensorData_device_DeviceID",
                table: "sensorData");

            migrationBuilder.DropForeignKey(
                name: "FK_sensorDataReport_device_deviceID",
                table: "sensorDataReport");

            migrationBuilder.DropForeignKey(
                name: "FK_user_userGroup_UserGroupID",
                table: "user");

            migrationBuilder.DropForeignKey(
                name: "FK_userGroup_userRole_UserRoleID",
                table: "userGroup");

            migrationBuilder.DropForeignKey(
                name: "FK_userRole_userRoleScope_RoleScopeID",
                table: "userRole");

            migrationBuilder.DropIndex(
                name: "IX_userRole_RoleScopeID",
                table: "userRole");

            migrationBuilder.DropIndex(
                name: "IX_userGroup_UserRoleID",
                table: "userGroup");

            migrationBuilder.DropIndex(
                name: "IX_user_UserGroupID",
                table: "user");

            migrationBuilder.DropIndex(
                name: "IX_sensorDataReport_deviceID",
                table: "sensorDataReport");

            migrationBuilder.DropIndex(
                name: "IX_sensorData_DeviceUnitID",
                table: "sensorData");

            migrationBuilder.DropIndex(
                name: "IX_sensorData_DeviceUnitZoneID",
                table: "sensorData");

            migrationBuilder.DropIndex(
                name: "IX_deviceUnit_DeviceUnitZoneID",
                table: "deviceUnit");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorBarometer",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorBattery",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorCo2",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorHumid",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorLight",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorMoist",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorPH",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorRainLevel",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorTemp",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorTempSoil",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorTvoc",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorWaterLevel",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigSensor_SensorWind",
                table: "deviceConfigSensor");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay1",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay2",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay3",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay4",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay5",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay6",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay7",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_deviceConfigController_Relay8",
                table: "deviceConfigController");

            migrationBuilder.DropIndex(
                name: "IX_device_DeviceConfigControllerID",
                table: "device");

            migrationBuilder.DropIndex(
                name: "IX_device_DeviceConfigSensorID",
                table: "device");

            migrationBuilder.DropIndex(
                name: "IX_device_DeviceTypeID",
                table: "device");

            migrationBuilder.DropIndex(
                name: "IX_device_DeviceTypeServiceID",
                table: "device");

            migrationBuilder.DropIndex(
                name: "IX_device_DeviceUnitID",
                table: "device");

            migrationBuilder.DropIndex(
                name: "IX_device_TenantID",
                table: "device");
        }
    }
}
