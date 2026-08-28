namespace api.Schema
{
    /// <summary>
    /// Git-versioned copy of the Agrumy database structure (tables + stored routines).
    ///
    /// Extracted from <c>api/agrumyDB-final.sql</c> - structure only, no data. The goal is that
    /// the full schema every <see cref="api.Dal.SqlRepository"/> call depends on lives here in
    /// source control, not only inside a live database. Used by
    /// <see cref="api.Dal.Interface.IRepository.EnsureSchemaAsync"/> to auto-provision an empty
    /// database on startup.
    ///
    /// Normalisation applied vs. the raw mysqldump:
    ///  - dump directives (<c>/*!40101 ... */</c>, <c>LOCK/UNLOCK TABLES</c>, <c>DROP TABLE</c>,
    ///    <c>ALTER DATABASE ... CHARACTER SET</c>, <c>DELIMITER</c>) removed;
    ///  - <c>CREATE TABLE</c> -&gt; <c>CREATE TABLE IF NOT EXISTS</c>, table-level
    ///    <c>AUTO_INCREMENT=n</c> options dropped;
    ///  - <c>CREATE DEFINER=`agrumy`@`%` PROCEDURE</c> -&gt; <c>CREATE OR REPLACE PROCEDURE</c>
    ///    (MariaDB syntax - the target server is MariaDB 11.x);
    ///  - tables are ordered so foreign keys resolve; provisioning also wraps the batch in
    ///    <c>SET FOREIGN_KEY_CHECKS = 0/1</c> as a belt-and-braces measure.
    ///
    /// Test-only routines from the dump (<c>_JsonExample</c>, <c>_sensorDataTest</c>,
    /// <c>_testproc</c>, <c>SensorDataReportTest</c>) are intentionally excluded.
    ///
    /// TODO: <c>ServerConfigUpdate</c> is referenced by name from
    /// <see cref="api.Dal.SqlRepository"/> (private <c>ServerConfigUpdate</c> method) but is NOT
    /// present in agrumyDB-final.sql. Add its <c>CREATE PROCEDURE</c> here once supplied from the
    /// production database.
    /// </summary>
    public static class SchemaScripts
    {
        /// <summary>Table whose presence is used to decide whether the schema needs provisioning.</summary>
        public const string KeyTable = "device";

        // ---------------------------------------------------------------------
        // TABLES (foreign-key safe order)
        // ---------------------------------------------------------------------

        private const string T_Tenant =
"""
CREATE TABLE IF NOT EXISTS `tenant` (
  `IDTenant` int(11) NOT NULL AUTO_INCREMENT,
  `TenantName` varchar(100) NOT NULL,
  `DateCreated` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`IDTenant`),
  UNIQUE KEY `Name_UNIQUE` (`TenantName`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceUnitZone =
"""
CREATE TABLE IF NOT EXISTS `deviceUnitZone` (
  `IDDeviceUnitZone` int(11) NOT NULL,
  `DeviceUnitZoneName` varchar(120) DEFAULT NULL,
  PRIMARY KEY (`IDDeviceUnitZone`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceUnit =
"""
CREATE TABLE IF NOT EXISTS `deviceUnit` (
  `IDDeviceUnit` int(11) NOT NULL,
  `DeviceUnitZoneID` int(11) DEFAULT NULL,
  `DeviceUnitName` varchar(100) DEFAULT NULL,
  `ZoneEnabled` bit(1) DEFAULT b'0',
  PRIMARY KEY (`IDDeviceUnit`),
  KEY `fk_deviceUnit_deviceUnitZone_idx` (`DeviceUnitZoneID`),
  CONSTRAINT `fk_deviceUnit_deviceUnitZone` FOREIGN KEY (`DeviceUnitZoneID`) REFERENCES `deviceUnitZone` (`IDDeviceUnitZone`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceType =
"""
CREATE TABLE IF NOT EXISTS `deviceType` (
  `IDDeviceType` int(11) NOT NULL AUTO_INCREMENT,
  `DeviceTypeName` varchar(100) DEFAULT NULL,
  `SensorEnabled` tinyint(1) DEFAULT NULL,
  `ControllerEnabled` tinyint(1) DEFAULT NULL,
  PRIMARY KEY (`IDDeviceType`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceTypeRelay =
"""
CREATE TABLE IF NOT EXISTS `deviceTypeRelay` (
  `IDDeviceTypeRelay` int(11) NOT NULL,
  `RelayName` varchar(128) DEFAULT NULL,
  PRIMARY KEY (`IDDeviceTypeRelay`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_520_ci;
""";

        private const string T_DeviceTypeSensor =
"""
CREATE TABLE IF NOT EXISTS `deviceTypeSensor` (
  `IDDeviceTypeSensor` int(11) NOT NULL,
  `SensorName` varchar(128) DEFAULT NULL,
  `SensorDescription` text DEFAULT NULL,
  `Battery` int(11) DEFAULT 0,
  `Temperature` int(11) DEFAULT 0,
  `TemperatureSoil` int(11) DEFAULT 0,
  `Humidity` int(11) DEFAULT 0,
  `Moisture` int(11) DEFAULT 0,
  `Light` int(11) DEFAULT 0,
  `Co2` int(11) DEFAULT 0,
  `Tvoc` int(11) DEFAULT 0,
  `Barometer` int(11) DEFAULT 0,
  `WaterPH` int(11) DEFAULT 0,
  `WaterTankLevel` int(11) DEFAULT 0,
  `RainLevel` int(11) DEFAULT 0,
  `Wind` int(11) DEFAULT 0,
  PRIMARY KEY (`IDDeviceTypeSensor`),
  KEY `fk_deviceTypeSensor_deviceConfigSensor_battery_idx` (`Battery`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_520_ci;
""";

        private const string T_DeviceTypeService =
"""
CREATE TABLE IF NOT EXISTS `deviceTypeService` (
  `IDDeviceTypeService` int(11) NOT NULL,
  `ServiceType` varchar(5) DEFAULT 'HTTPS',
  PRIMARY KEY (`IDDeviceTypeService`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceConfigController =
"""
CREATE TABLE IF NOT EXISTS `deviceConfigController` (
  `IDDeviceConfigController` int(11) NOT NULL AUTO_INCREMENT,
  `TempLow` double DEFAULT 5,
  `TempHigh` double DEFAULT 30,
  `HumidLow` double DEFAULT 35,
  `HumidHigh` double DEFAULT 80,
  `MoistLow` double DEFAULT 20,
  `MoistHigh` double DEFAULT 80,
  `LightLow` double DEFAULT 20,
  `LightHigh` double DEFAULT 100,
  `WaterLow` double DEFAULT 10,
  `WaterHigh` double DEFAULT 95,
  `RelayEnabled` tinyint(1) DEFAULT 0,
  `Relay1` int(11) DEFAULT 1,
  `Relay2` int(11) DEFAULT 2 COMMENT 'Relay value is PIN value',
  `Relay3` int(11) DEFAULT 3,
  `Relay4` int(11) DEFAULT 4,
  `Relay5` int(11) DEFAULT 0,
  `Relay6` int(11) DEFAULT 0,
  `Relay7` int(11) DEFAULT 0,
  `Relay8` int(11) DEFAULT 0,
  PRIMARY KEY (`IDDeviceConfigController`),
  KEY `fk_deviceConfigController_idx` (`Relay1`,`Relay3`,`Relay2`,`Relay4`,`Relay5`,`Relay6`,`Relay7`,`Relay8`),
  KEY `fk_deviceConfigController_relay2_idx` (`Relay2`),
  KEY `fk_deviceConfigController_relay3_idx` (`Relay3`),
  KEY `fk_deviceConfigController_relay4_idx` (`Relay4`),
  KEY `fk_deviceConfigController_relay5_idx` (`Relay5`),
  KEY `fk_deviceConfigController_relay6_idx` (`Relay6`),
  KEY `fk_deviceConfigController_relay7_idx` (`Relay7`),
  KEY `fk_deviceConfigController_relay8_idx` (`Relay8`),
  CONSTRAINT `fk_deviceConfigController_relay1` FOREIGN KEY (`Relay1`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay2` FOREIGN KEY (`Relay2`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay3` FOREIGN KEY (`Relay3`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay4` FOREIGN KEY (`Relay4`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay5` FOREIGN KEY (`Relay5`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay6` FOREIGN KEY (`Relay6`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay7` FOREIGN KEY (`Relay7`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigController_relay8` FOREIGN KEY (`Relay8`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_unicode_ci;
""";

        private const string T_DeviceConfigSensor =
"""
CREATE TABLE IF NOT EXISTS `deviceConfigSensor` (
  `IDDeviceConfigSensor` int(11) NOT NULL AUTO_INCREMENT,
  `SensorBattery` int(11) DEFAULT 0,
  `SensorTemp` int(11) DEFAULT 0,
  `SensorTempSoil` int(11) DEFAULT 0,
  `SensorHumid` int(11) DEFAULT 0,
  `SensorMoist` int(11) DEFAULT 0,
  `SensorLight` int(11) DEFAULT 0,
  `SensorCo2` int(11) DEFAULT 0,
  `SensorTvoc` int(11) DEFAULT 0,
  `SensorBarometer` int(11) DEFAULT 0,
  `SensorPH` int(11) DEFAULT 0,
  `SensorRainLevel` int(11) DEFAULT 0,
  `SensorWaterLevel` int(11) DEFAULT 0,
  `SensorWind` int(11) DEFAULT 0,
  PRIMARY KEY (`IDDeviceConfigSensor`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_battery_idx` (`SensorBattery`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_temp_idx` (`SensorTemp`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_tempSoil_idx` (`SensorTempSoil`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_humid_idx` (`SensorHumid`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_moist_idx` (`SensorMoist`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_light_idx` (`SensorLight`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_co2_idx` (`SensorCo2`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_tvoc_idx` (`SensorTvoc`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_barometer_idx` (`SensorBarometer`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_ph_idx` (`SensorPH`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_rainLevel_idx` (`SensorRainLevel`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_waterLevel_idx` (`SensorWaterLevel`),
  KEY `fk_deviceConfigSensor_deviceTypeSensor_wind_idx` (`SensorWind`),
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_barometer` FOREIGN KEY (`SensorBarometer`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_battery` FOREIGN KEY (`SensorBattery`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_co2` FOREIGN KEY (`SensorCo2`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_humid` FOREIGN KEY (`SensorHumid`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_light` FOREIGN KEY (`SensorLight`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_moist` FOREIGN KEY (`SensorMoist`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_ph` FOREIGN KEY (`SensorPH`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_rainLevel` FOREIGN KEY (`SensorRainLevel`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_temp` FOREIGN KEY (`SensorTemp`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_tempSoil` FOREIGN KEY (`SensorTempSoil`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_tvoc` FOREIGN KEY (`SensorTvoc`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_waterLevel` FOREIGN KEY (`SensorWaterLevel`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_deviceConfigSensor_deviceTypeSensor_wind` FOREIGN KEY (`SensorWind`) REFERENCES `deviceTypeSensor` (`IDDeviceTypeSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci COMMENT='x';
""";

        private const string T_UserRoleScope =
"""
CREATE TABLE IF NOT EXISTS `userRoleScope` (
  `IDRoleScope` int(11) NOT NULL AUTO_INCREMENT,
  `RoleScopeName` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`IDRoleScope`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_UserRole =
"""
CREATE TABLE IF NOT EXISTS `userRole` (
  `IDUserRole` int(11) NOT NULL AUTO_INCREMENT,
  `RoleName` varchar(45) DEFAULT NULL,
  `RoleScopeID` int(11) DEFAULT NULL,
  PRIMARY KEY (`IDUserRole`),
  KEY `fk_userRole_userRoleScope_idx` (`RoleScopeID`),
  CONSTRAINT `fk_userRole_userRoleScope` FOREIGN KEY (`RoleScopeID`) REFERENCES `userRoleScope` (`IDRoleScope`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_UserGroup =
"""
CREATE TABLE IF NOT EXISTS `userGroup` (
  `IDUserGroup` int(11) NOT NULL AUTO_INCREMENT,
  `GroupName` varchar(128) DEFAULT NULL,
  `UserRoleID` int(11) DEFAULT NULL,
  PRIMARY KEY (`IDUserGroup`),
  KEY `fk_userGroup_userRole_idx` (`UserRoleID`),
  CONSTRAINT `fk_userGroup_userRole` FOREIGN KEY (`UserRoleID`) REFERENCES `userRole` (`IDUserRole`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_520_ci;
""";

        private const string T_User =
"""
CREATE TABLE IF NOT EXISTS `user` (
  `IDUser` int(11) NOT NULL AUTO_INCREMENT,
  `TenantID` int(11) NOT NULL DEFAULT 0,
  `Email` varchar(100) NOT NULL,
  `Username` varchar(100) DEFAULT NULL,
  `PwdHash` text NOT NULL,
  `PwdSalt` varchar(128) NOT NULL,
  `DevicePin` int(11) DEFAULT NULL,
  `FirstName` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `LastName` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `Phone` varchar(15) DEFAULT NULL COMMENT 'International standards can support up to 15 digits',
  `UserGroupID` int(11) DEFAULT 0,
  `Enabled` tinyint(1) DEFAULT 0,
  `DateCreated` datetime DEFAULT current_timestamp(),
  `DateModified` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`IDUser`),
  UNIQUE KEY `email_UNIQUE` (`Email`),
  UNIQUE KEY `Username_UNIQUE` (`Username`),
  KEY `fk_userGroup_idx` (`UserGroupID`),
  CONSTRAINT `fk_userGroup` FOREIGN KEY (`UserGroupID`) REFERENCES `userGroup` (`IDUserGroup`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_Device =
"""
CREATE TABLE IF NOT EXISTS `device` (
  `IDDevice` int(11) NOT NULL AUTO_INCREMENT,
  `TenantID` int(11) DEFAULT 0,
  `DeviceTypeID` int(11) DEFAULT 0,
  `DeviceUnitID` int(11) DEFAULT 0,
  `DeviceUnitZoneID` int(11) DEFAULT 0,
  `DeviceConfigSensorID` int(11) DEFAULT NULL,
  `DeviceConfigControllerID` int(11) DEFAULT NULL,
  `DeviceTypeServiceID` int(11) DEFAULT 1 COMMENT 'HTTP OR MQTT',
  `DeviceName` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `MacAddress` varchar(12) DEFAULT NULL,
  `ApiId` varchar(128) CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL,
  `ApiKey` varchar(128) CHARACTER SET latin1 COLLATE latin1_swedish_ci NOT NULL,
  `ServicePoint` varchar(200) CHARACTER SET latin1 COLLATE latin1_swedish_ci DEFAULT 'api.agrumy.com',
  `ServicePublicKey` text CHARACTER SET latin1 COLLATE latin1_swedish_ci DEFAULT NULL,
  `SleepSeconds` int(11) DEFAULT 60,
  `SleepDeepEnabled` tinyint(1) DEFAULT 0,
  `DeviceSensorEnabled` tinyint(1) DEFAULT 0,
  `DeviceControllerEnabled` tinyint(1) DEFAULT 0,
  `BatteryEnabled` tinyint(1) DEFAULT 0,
  `Enabled` tinyint(1) DEFAULT 0,
  `Debug` tinyint(1) DEFAULT 1,
  `Reboot` tinyint(1) DEFAULT 0,
  `Reset` tinyint(1) DEFAULT 0,
  `FirmwareUpdate` tinyint(1) DEFAULT 0,
  `ConfigVersion` int(11) DEFAULT 0,
  `DateCreated` datetime DEFAULT current_timestamp(),
  `DateModified` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`IDDevice`),
  UNIQUE KEY `ApiID_UNIQUE` (`ApiId`),
  KEY `fk_device_deviceType_idx` (`DeviceTypeID`),
  KEY `fk_device_deviceConfigController_idx` (`DeviceConfigControllerID`),
  KEY `fk_device_deviceConfigSensor_idx` (`DeviceConfigSensorID`),
  KEY `fk_device_unit_idx` (`DeviceUnitID`),
  KEY `fk_device_tenant_idx` (`TenantID`),
  KEY `fk_device_deviceTypeService_idx` (`DeviceTypeServiceID`),
  CONSTRAINT `fk_device_deviceConfigController` FOREIGN KEY (`DeviceConfigControllerID`) REFERENCES `deviceConfigController` (`IDDeviceConfigController`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_device_deviceConfigSensor` FOREIGN KEY (`DeviceConfigSensorID`) REFERENCES `deviceConfigSensor` (`IDDeviceConfigSensor`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_device_deviceType` FOREIGN KEY (`DeviceTypeID`) REFERENCES `deviceType` (`IDDeviceType`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_device_deviceTypeService` FOREIGN KEY (`DeviceTypeServiceID`) REFERENCES `deviceTypeService` (`IDDeviceTypeService`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_device_tenant` FOREIGN KEY (`TenantID`) REFERENCES `tenant` (`IDTenant`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_device_unit` FOREIGN KEY (`DeviceUnitID`) REFERENCES `deviceUnit` (`IDDeviceUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
""";

        private const string T_SensorData =
"""
CREATE TABLE IF NOT EXISTS `sensorData` (
  `IDSensorData` int(11) NOT NULL AUTO_INCREMENT,
  `TenantID` int(11) NOT NULL,
  `DeviceID` int(11) NOT NULL,
  `DeviceUnitID` int(11) NOT NULL,
  `DeviceUnitZoneID` int(11) NOT NULL,
  `Battery` tinyint(1) DEFAULT NULL,
  `Temperature` double DEFAULT NULL,
  `SoilTemperature` double DEFAULT NULL,
  `Humidity` double DEFAULT NULL,
  `Moisture` tinyint(1) DEFAULT NULL,
  `Light` int(11) DEFAULT NULL,
  `Co2` int(11) DEFAULT NULL,
  `Tvoc` int(11) DEFAULT NULL,
  `Barometer` double DEFAULT NULL,
  `LiquidPH` double DEFAULT NULL,
  `RainLevel` int(11) DEFAULT NULL,
  `WaterLevel` tinyint(1) DEFAULT NULL,
  `Wind` int(11) DEFAULT NULL,
  `DateCreated` datetime DEFAULT NULL,
  PRIMARY KEY (`IDSensorData`),
  KEY `fk_sensorData_tenant_idx` (`TenantID`),
  KEY `fk_sensorData_device_idx` (`DeviceID`),
  KEY `fk_sensorData_deviceUnit_idx` (`DeviceUnitID`),
  KEY `fk_sensorData_deviceUnitZone_idx` (`DeviceUnitZoneID`),
  CONSTRAINT `fk_sensorData_device` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_sensorData_deviceUnit` FOREIGN KEY (`DeviceUnitID`) REFERENCES `deviceUnit` (`IDDeviceUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `fk_sensorData_deviceUnitZone` FOREIGN KEY (`DeviceUnitZoneID`) REFERENCES `deviceUnitZone` (`IDDeviceUnitZone`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_SensorDataReport =
"""
CREATE TABLE IF NOT EXISTS `sensorDataReport` (
  `IDSensorDataReport` int(11) NOT NULL AUTO_INCREMENT,
  `deviceID` int(11) DEFAULT NULL,
  `ReportName` varchar(128) DEFAULT NULL,
  `DateGenerated` datetime DEFAULT current_timestamp(),
  `sensorData` longtext DEFAULT NULL,
  PRIMARY KEY (`IDSensorDataReport`),
  KEY `fk_sensorDataReport_device_idx` (`deviceID`),
  CONSTRAINT `fk_sensorDataReport_device` FOREIGN KEY (`deviceID`) REFERENCES `device` (`IDDevice`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_DeviceFirmware =
"""
CREATE TABLE IF NOT EXISTS `deviceFirmware` (
  `IDDeviceFirmware` int(11) NOT NULL,
  `DeviceTypeID` int(11) DEFAULT NULL,
  `Version` decimal(10,0) DEFAULT NULL,
  `Url` text DEFAULT NULL,
  PRIMARY KEY (`IDDeviceFirmware`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_EventDevice =
"""
CREATE TABLE IF NOT EXISTS `eventDevice` (
  `IDEventDevice` int(11) NOT NULL AUTO_INCREMENT,
  `DeviceID` int(11) NOT NULL,
  `EventID` int(11) NOT NULL,
  `Date` datetime DEFAULT NULL,
  `Message` text DEFAULT NULL,
  PRIMARY KEY (`IDEventDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_EventService =
"""
CREATE TABLE IF NOT EXISTS `eventService` (
  `IDEventService` int(11) NOT NULL AUTO_INCREMENT,
  `ServiceID` int(11) NOT NULL,
  `EventID` int(11) NOT NULL,
  `Date` datetime DEFAULT NULL,
  `Message` text DEFAULT NULL,
  PRIMARY KEY (`IDEventService`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        private const string T_ServerConfig =
"""
CREATE TABLE IF NOT EXISTS `serverConfig` (
  `IDServerConfig` int(11) NOT NULL,
  `ServerConfigName` varchar(100) DEFAULT NULL,
  `ConfigKey` varchar(128) NOT NULL COMMENT 'Salt key',
  `JWTKey` text DEFAULT NULL,
  `PortHTTP` int(11) DEFAULT NULL,
  `PortHTTPS` int(11) DEFAULT 80,
  `serverConfigcol` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`IDServerConfig`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
""";

        // ---------------------------------------------------------------------
        // STORED PROCEDURES (referenced by name from SqlRepository)
        // ---------------------------------------------------------------------

        private const string P_ServerConfig =
"""
CREATE OR REPLACE PROCEDURE `ServerConfig`(
idServerConfig int
)
BEGIN
SELECT * FROM serverConfig WHERE serverConfig.IDServerConfig = idServerConfig;
END
""";

        private const string P_ServerConfigGet =
"""
CREATE OR REPLACE PROCEDURE `ServerConfigGet`(
idServerConfig int
)
BEGIN
SELECT * FROM serverConfig WHERE serverConfig.IDServerConfig = idServerConfig;
END
""";

        private const string P_ServerConfigAdd =
"""
CREATE OR REPLACE PROCEDURE `ServerConfigAdd`(
	idServerConfig int,
    serverConfigName nvarchar(100),
	configKey nvarchar(128),
	portHTTP int,
    portHTTPS int)
BEGIN
INSERT
	INTO serverConfig(IDServerConfig, ServerConfigName, ConfigKey, PortHTTP, PortHTTPS)
	VALUES (idServerConfig,serverConfigName, configKey, portHTTP, portHTTPS);
END
""";

        private const string P_TenantGet =
"""
CREATE OR REPLACE PROCEDURE `TenantGet`(
	tenantName nvarchar(100)
	)
BEGIN
select * from tenant
WHERE
	tenant.TenantName = tenantName;
END
""";

        private const string P_TenantAdd =
"""
CREATE OR REPLACE PROCEDURE `TenantAdd`(
	tenantName nvarchar(100)
	)
BEGIN
INSERT INTO tenant (TenantName)
	VALUES (tenantName);
	SELECT LAST_INSERT_ID();
END
""";

        private const string P_UserAdd =
"""
CREATE OR REPLACE PROCEDURE `UserAdd`(
	tenantID int,
	email nvarchar(128),
    devicePin int,
    username nvarchar(128),
    pwdHash nvarchar(128),
    pwdSalt nvarchar(129),
	firstName nvarchar(128),
	lastName nvarchar(128),
    phone nvarchar(15),
    userGroupID int,
    enabled bool
    )
BEGIN
INSERT
	INTO user(TenantID, Email, DevicePin, Username, PwdHash, PwdSalt, FirstName, LastName, Phone, UserGroupID, Enabled)
	VALUES (tenantID, email, devicePin, username, pwdhash, pwdsalt, firstName, lastName, phone, userGroupID, enabled);
END
""";

        private const string P_UserDelete =
"""
CREATE OR REPLACE PROCEDURE `UserDelete`(
	idUser int
)
BEGIN
	IF  (idUser>1) -- protecting default admin and default user
	THEN
		DELETE FROM user where user.IDUser = idUser;
        SELECT ROW_COUNT(); -- return rows if affected
	END IF;
END
""";

        private const string P_UserGet =
"""
CREATE OR REPLACE PROCEDURE `UserGet`(
	idUser int,
    email varchar(128),
    username varchar(128)
)
BEGIN
IF (idUser IS NOT NULL) THEN
	BEGIN
		SELECT * FROM user
        inner join userGroup on userGroup.IDUserGroup=user.UserGroupID
        WHERE user.IDUser = idUser
        limit 1;
	END;
END IF;

IF (idUser IS NULL AND email IS NOT NULL AND username IS NULL) THEN
	BEGIN
		SELECT * FROM user
        inner join userGroup on userGroup.IDUserGroup=user.UserGroupID
        WHERE user.Email = email
        limit 1;
    END;
END IF;

IF (idUser IS NULL AND email IS NULL AND username IS NOT NULL) THEN
	BEGIN
		SELECT * FROM user
        inner join userGroup on userGroup.IDUserGroup=user.UserGroupID
        WHERE user.Username = username
        limit 1;
    END;
END IF;

END
""";

        private const string P_UsersGet =
"""
CREATE OR REPLACE PROCEDURE `UsersGet`(
	tenantID int
)
BEGIN
	select * from user
    join userGroup on userGroup.IDUserGroup = user.UserGroupID
    where user.TenantID = tenantID;
END
""";

        private const string P_UserUpdate =
"""
CREATE OR REPLACE PROCEDURE `UserUpdate`(
	idUser int,
	tenantID int,
	email nvarchar(128),
    devicePin int,
    username nvarchar(128),
	firstName nvarchar(128),
	lastName nvarchar(128),
    phone nvarchar(15),
    userGroupID int,
    enabled bool
    )
BEGIN
UPDATE user
SET
	user.TenantID = tenantID,
	user.Email = email,
    user.DevicePin = devicePin,
    user.Username = username,
	user.FirstName = firstName,
	user.LastName = lastName,
    user.Phone = phone,
    user.userGroupID = userGroupID,
    user.Enabled = enabled
Where user.IDUser = idUser;
END
""";

        private const string P_UserSetPassword =
"""
CREATE OR REPLACE PROCEDURE `UserSetPassword`(
	email nvarchar(128),
    pwdHash text,
    pwdSalt nvarchar(128)
	)
BEGIN
UPDATE user
SET user.PwdHash = pwdHash, user.PwdSalt = pwdSalt
WHERE user.Email = email;

SELECT ROW_COUNT(); -- return rows if affected

END
""";

        private const string P_UserSecretGet =
"""
CREATE OR REPLACE PROCEDURE `UserSecretGet`(
	idUser int,
    email varchar(128),
    username varchar(128)
)
BEGIN
IF (idUser IS NOT NULL) THEN
	BEGIN
		SELECT PwdHash, PwdSalt FROM user
        WHERE user.IDUser = idUser
        limit 1;
	END;
END IF;

IF (idUser IS NULL AND email IS NOT NULL AND username IS NULL) THEN
	BEGIN
		SELECT PwdHash, PwdSalt FROM user
        WHERE user.Email = email
        limit 1;
    END;
END IF;

IF (idUser IS NULL AND email IS NULL AND username IS NOT NULL) THEN
	BEGIN
		SELECT PwdHash, PwdSalt FROM user
        WHERE user.Username = username
        limit 1;
    END;
END IF;

END
""";

        private const string P_UserRoleGet =
"""
CREATE OR REPLACE PROCEDURE `UserRoleGet`()
BEGIN
	select * from userRole;
END
""";

        private const string P_UserGroupAdd =
"""
CREATE OR REPLACE PROCEDURE `UserGroupAdd`(
	groupName varchar(128),
    userRoleID int
)
BEGIN
INSERT INTO userGroup (GroupName, UserRoleID)
	VALUES (groupName,userRoleID);
	SELECT LAST_INSERT_ID();
END
""";

        private const string P_UserGroupDelete =
"""
CREATE OR REPLACE PROCEDURE `UserGroupDelete`(
	idUserGroup int
)
BEGIN
	IF  (idUserGroup>0) -- protecting default admin and default user
	THEN
		DELETE FROM userGroup where userGroup.IDUserGroup = idUserGroup;
        SELECT ROW_COUNT(); -- return rows if affected
	END IF;
END
""";

        private const string P_UserGroupGet =
"""
CREATE OR REPLACE PROCEDURE `UserGroupGet`(
	idUserGroup int
)
BEGIN
	select * from userGroup
    join userRole on userRole.IDUserRole = userGroup.UserRoleID
    where userGroup.IDUserGroup = idUserGroup;
END
""";

        private const string P_UserGroupsGet =
"""
CREATE OR REPLACE PROCEDURE `UserGroupsGet`()
BEGIN
	select * from userGroup
    join userRole on userRole.IDUserRole = userGroup.UserRoleID;
END
""";

        private const string P_DeviceAdd =
"""
CREATE OR REPLACE PROCEDURE `DeviceAdd`(
	tenantID int,
    deviceTypeID int,
    deviceUnitID int,
    deviceUnitZoneID int,
    deviceName varchar(128),
    macAddress varchar(12),
    apiId varchar(36),
    apiKey varchar(36),
    servicePoint varchar(500),
    deviceTypeServiceID int,
    deviceSensorEnabled int,
    deviceControllerEnabled int,
    batteryEnabled int,
    enabled int,
    configVersion int
)
BEGIN
	DECLARE deviceConfigSensorID int;
	DECLARE deviceConfigControllerID int;

	INSERT INTO deviceConfigSensor () VALUES ();
	SELECT LAST_INSERT_ID() INTO deviceConfigSensorID;

	INSERT INTO deviceConfigController () VALUES ();
	SELECT LAST_INSERT_ID() INTO deviceConfigControllerID;

    INSERT
		INTO device (
						TenantID,
						DeviceTypeID,
                        DeviceUnitID,
                        DeviceUnitZoneID,
                        DeviceName,
                        MacAddress,
                        ApiId,
                        ApiKey,
                        ServicePoint,
                        DeviceTypeServiceID,
                        DeviceSensorEnabled,
                        DeviceConfigSensorID,
                        DeviceControllerEnabled,
                        DeviceConfigControllerID,
                        BatteryEnabled,
                        Enabled,
                        ConfigVersion
                        )
		VALUES (
        				tenantID,
						deviceTypeID,
                        deviceUnitID,
                        deviceUnitZoneID,
                        deviceName,
                        macAddress,
                        apiId,
                        apiKey,
                        servicePoint,
                        deviceTypeServiceID,
                        deviceSensorEnabled,
                        deviceConfigSensorID,
                        deviceControllerEnabled,
                        deviceConfigControllerID,
                        batteryEnabled,
                        enabled,
                        configVersion
                        ) ;
END
""";

        private const string P_DeviceCheckMacAddress =
"""
CREATE OR REPLACE PROCEDURE `DeviceCheckMacAddress`(
	tenantID int,
    macAddress varchar(18)
)
BEGIN
	select 1 from device where device.TenantID = tenantID and device.MacAddress = macAddress Limit 1;
END
""";

        private const string P_DeviceConfigControllerGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceConfigControllerGet`(
	deviceConfigControllerID int
)
BEGIN
	select * from deviceConfigController where deviceConfigController.IDDeviceConfigController = deviceConfigControllerID LIMIT 1;
END
""";

        private const string P_DeviceGetByDeviceConfigSensorId =
"""
CREATE OR REPLACE PROCEDURE `DeviceGetByDeviceConfigSensorId`(
	deviceConfigSensorID int
)
BEGIN
-- No tenant filter by design - used only for ownership checks before returning config data.
select * from agrumy.device where device.DeviceConfigSensorID = deviceConfigSensorID Limit 1;
END
""";

        private const string P_DeviceGetByDeviceConfigControllerId =
"""
CREATE OR REPLACE PROCEDURE `DeviceGetByDeviceConfigControllerId`(
	deviceConfigControllerID int
)
BEGIN
-- No tenant filter by design - used only for ownership checks before returning config data.
select * from agrumy.device where device.DeviceConfigControllerID = deviceConfigControllerID Limit 1;
END
""";

        private const string P_DeviceConfigControllerUpdate =
"""
CREATE OR REPLACE PROCEDURE `DeviceConfigControllerUpdate`(
	idDevice int,
	idDeviceConfigController int,
	tempLow int,
    tempHigh int,
    humidLow int,
    humidHigh int,
    moistLow int,
    moistHigh int,
    lightLow int,
    lightHigh int,
    waterLow int,
    waterHigh int,
    relayEnabled bool,
    relay1 int,
    relay2 int,
    relay3 int,
    relay4 int,
    relay5 int,
    relay6 int,
    relay7 int,
    relay8 int
)
BEGIN
UPDATE deviceConfigController
SET
    deviceConfigController.TempLow =  tempLow,
    deviceConfigController.TempHigh =  tempHigh,
    deviceConfigController.HumidLow =  humidLow,
    deviceConfigController.HumidHigh =  humidHigh,
	deviceConfigController.MoistLow =  moistLow,
    deviceConfigController.MoistHigh =  moistHigh,
    deviceConfigController.LightLow =  lightLow,
    deviceConfigController.LightHigh =  lightHigh,
    deviceConfigController.WaterLow =  waterLow,
    deviceConfigController.WaterHigh =  waterHigh,

    deviceConfigController.RelayEnabled =  relayEnabled,
    deviceConfigController.Relay1 =  relay1,
    deviceConfigController.Relay2 =  relay2,
    deviceConfigController.Relay3 =  relay3,
    deviceConfigController.Relay4 =  relay4,
    deviceConfigController.Relay5 =  relay5,
    deviceConfigController.Relay6 =  relay6,
    deviceConfigController.Relay7 =  relay7,
    deviceConfigController.Relay8 =  relay8

WHERE
	deviceConfigController.IDDeviceConfigController = idDeviceConfigController;

-- UPDATE CONFIG VERSION
UPDATE device SET ConfigVersion = ConfigVersion + 1 WHERE device.IDDevice=idDevice;
END
""";

        private const string P_DeviceConfigSensorGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceConfigSensorGet`(
	deviceConfigSensorID int
)
BEGIN
	select * from deviceConfigSensor
	WHERE deviceConfigSensor.IDDeviceConfigSensor = deviceConfigSensorID
	LIMIT 1;

END
""";

        private const string P_DeviceConfigSensorUpdate =
"""
CREATE OR REPLACE PROCEDURE `DeviceConfigSensorUpdate`(
	idDevice int,
    IDDeviceConfigSensor int,
    sensorBattery int,
    sensorTemp int,
    sensorTempSoil int,
    sensorHumid int,
    sensorMoist int,
    sensorLight int,
    sensorCo2 int,
    sensorTvoc int,
    sensorBarometer int,
    sensorPH int,
    sensorRainLevel int,
    sensorWaterLevel int,
    sensorWind int
)
BEGIN
UPDATE deviceConfigSensor
SET

    deviceConfigSensor.SensorBattery = SensorBattery,
    deviceConfigSensor.SensorTemp = SensorTemp,
    deviceConfigSensor.SensorTempSoil = SensorTempSoil,
    deviceConfigSensor.SensorHumid = SensorHumid,
    deviceConfigSensor.SensorMoist = SensorMoist,
    deviceConfigSensor.SensorLight = SensorLight,
    deviceConfigSensor.SensorCo2 = SensorCo2,
    deviceConfigSensor.SensorTvoc = SensorTvoc,
    deviceConfigSensor.SensorBarometer = SensorBarometer,
    deviceConfigSensor.SensorPH = SensorPH,
    deviceConfigSensor.SensorRainLevel = SensorRainLevel,
    deviceConfigSensor.SensorWaterLevel = SensorWaterLevel,
    deviceConfigSensor.SensorWind = SensorWind

WHERE
	deviceConfigSensor.IDDeviceConfigSensor = idDeviceConfigSensor;

-- UPDATE CONFIG VERSION
UPDATE device SET ConfigVersion = ConfigVersion + 1 WHERE device.IDDevice=idDevice;
END
""";

        private const string P_DeviceDelete =
"""
CREATE OR REPLACE PROCEDURE `DeviceDelete`(
	idDevice int,
    tenantID int
)
BEGIN
    SELECT DeviceConfigSensorID
	INTO @DeviceConfigSensorID
	FROM device where device.IDDevice=idDevice AND device.TenantID=tenantID;

    SELECT DeviceConfigControllerID
	INTO @DeviceConfigControllerID
	FROM device where device.IDDevice=idDevice AND device.TenantID=tenantID;

	delete from device where device.IDDevice=idDevice AND device.TenantID=tenantID;
	delete from deviceConfigSensor where deviceConfigSensor.IDdeviceConfigSensor = @DeviceConfigSensorID ;
    delete from deviceConfigController where deviceConfigController.IDdeviceConfigController = @DeviceConfigControllerID;

END
""";

        private const string P_DeviceGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceGet`(
	tenantID int,
	idDevice int,
	apiID varchar(128),
    macAddress varchar(18)
)
BEGIN
IF (idDevice IS NOT NULL) THEN
	BEGIN
		SELECT * FROM agrumy.device where device.TenantID=tenantID and device.IDDevice=idDevice Limit 1;
	END;
END IF;

IF (idDevice IS NULL AND apiID IS NOT NULL AND macAddress IS NULL) THEN
	BEGIN
		SELECT * FROM agrumy.device where device.TenantID=tenantID and  device.ApiID=apiID Limit 1;
    END;
END IF;

IF (idDevice IS NULL AND apiID IS NULL AND macAddress IS NOT NULL) THEN
	BEGIN
		SELECT * FROM agrumy.device where device.TenantID=tenantID and  device.MacAddress=macAddress Limit 1;
    END;
END IF;

END
""";

        private const string P_DeviceGetById =
"""
CREATE OR REPLACE PROCEDURE `DeviceGetById`(
	idDevice int
)
BEGIN
-- No tenant filter by design - used only for ownership checks before an authorized write.
select * from agrumy.device where device.IDDevice = idDevice Limit 1;
END
""";

        private const string P_DevicesGet =
"""
CREATE OR REPLACE PROCEDURE `DevicesGet`(
	tenantID int
)
BEGIN
SELECT * FROM agrumy.device
where device.TenantID = tenantID;
END
""";

        private const string P_DeviceTypeGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceTypeGet`()
BEGIN
	select * from deviceType;
END
""";

        private const string P_DeviceTypeRelayGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceTypeRelayGet`()
BEGIN
	select * from deviceTypeRelay;
END
""";

        private const string P_DeviceTypeSensorGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceTypeSensorGet`()
BEGIN
	select * from deviceTypeSensor;
END
""";

        private const string P_DeviceTypeServiceGet =
"""
CREATE OR REPLACE PROCEDURE `DeviceTypeServiceGet`()
BEGIN
	select * from deviceTypeService;
END
""";

        private const string P_DeviceUpdate =
"""
CREATE OR REPLACE PROCEDURE `DeviceUpdate`(
	idDevice int,
    tenantID int,
    deviceTypeID int,
    deviceTypeServiceID int,
    deviceUnitID int,
    deviceUnitZoneID int,
    deviceName varchar(128),
    apiId varchar(128),
    apiKey varchar(128),
    servicePoint varchar(200),
    servicePublicKey text,
    sleepSeconds int,
    sleepDeepEnabled bool,
    deviceSensorEnabled bool,
    deviceControllerEnabled bool,
    batteryEnabled bool,
    enabled bool,
    debug bool,
    configVersion int
)
BEGIN
UPDATE device
SET
	device.TenantID = tenantID,
	device.DeviceTypeID = deviceTypeID,
    device.DeviceTypeServiceID = deviceTypeServiceID,
    device.DeviceUnitID = deviceUnitID,
    device.DeviceName = deviceName,
	device.ApiId = apiId,
	device.ApiKey = apiKey,
    device.ServicePoint = servicePoint,
    device.ServicePublicKey = servicePublicKey,
    device.SleepSeconds = sleepSeconds,
    device.SleepDeepEnabled = sleepDeepEnabled,
    device.DeviceSensorEnabled = deviceSensorEnabled,
    device.DeviceControllerEnabled = deviceControllerEnabled,
    device.BatteryEnabled = batteryEnabled,
    device.Enabled = enabled,
    device.Debug = debug,
    device.ConfigVersion = configVersion+1

Where device.IDDevice = idDevice;
END
""";

        private const string P_SensorDataDelete =
"""
CREATE OR REPLACE PROCEDURE `SensorDataDelete`(
	deviceID int,
	tenantID int,
    timeMDMY int,
    timeRange int

)
BEGIN

CASE timeMDMY
	WHEN 0 THEN
		delete from sensorData
		where DateCreated < CURRENT_TIMESTAMP - INTERVAL timeRange MINUTE AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
	WHEN 1 THEN
		delete from sensorData
        where DateCreated < CURRENT_TIMESTAMP - INTERVAL timeRange DAY AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
	WHEN 2 THEN
		delete from sensorData
        where DateCreated < CURRENT_TIMESTAMP - INTERVAL timeRange MONTH AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
	WHEN 3 THEN
		delete from sensorData
        where DateCreated < CURRENT_TIMESTAMP - INTERVAL timeRange YEAR AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
        -- ELSE delete  from sensorData LIMIT 0;
END CASE;
COMMIT;
END
""";

        private const string P_SensorDataGet =
"""
CREATE OR REPLACE PROCEDURE `SensorDataGet`(
    tenantID int,
	deviceID int,
    timeRange int,
    timeMDMY int,
    buildReport bool
)
BEGIN
	DECLARE sensorDataResult LONGTEXT;
	CALL `agrumy`.`SensorDataReportBuilder`(tenantID, deviceID, timeRange, timeMDMY, sensorDataResult);

    IF sensorDataResult IS NOT NULL AND buildReport > 0 THEN
		INSERT INTO sensorDataReport (deviceID,ReportName,sensorData)
		VALUES(1000038,CURRENT_TIMESTAMP(0),sensorDataResult);
    END IF;
    SELECT sensorDataResult;
END
""";

        private const string P_SensorDataGetJson =
"""
CREATE OR REPLACE PROCEDURE `SensorDataGetJson`(
	deviceID int,
    tenantID int,
    timeRange int,
    timeMDMY int
)
BEGIN

SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- nolock read, dirty read for sensor
START TRANSACTION;
-- 0 minute, 1 hour, 2 day, 3 year, add interval 1+

CASE timeMDMY
	WHEN 0 THEN
		SELECT JSON_ARRAYAGG(JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
        )) as sensorData
        FROM sensorData
		where DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange MINUTE AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;

	WHEN 1 THEN
		SELECT JSON_ARRAYAGG(JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
        )) as sensorData
        FROM sensorData
        where DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange DAY AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
	WHEN 2 THEN
		SELECT JSON_ARRAYAGG(JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
        )) as sensorData
        FROM sensorData
        where DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange MONTH AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
	WHEN 3 THEN
		SELECT JSON_ARRAYAGG(JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
        )) as sensorData
        FROM sensorData
        where DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange YEAR AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID;
        ELSE
        select null as sensorData  from sensorData LIMIT 0;
END CASE;
COMMIT;
END
""";

        private const string P_SensorDataPush =
"""
CREATE OR REPLACE PROCEDURE `SensorDataPush`(
	jsonData LONGTEXT
)
BEGIN
INSERT
	INTO sensorData (
	deviceID,
	tenantID,
	deviceUnitID,
    deviceUnitZoneID,
    battery,
    temperature,
    soilTemperature,
    humidity,
    moisture,
    light,
    co2,
    tvoc,
    barometer,
    liquidPH,
    rainlevel,
    waterLevel,
    wind,
    dateCreated
)
  SELECT
	j.deviceID,
	j.tenantID,
    j.deviceUnitID,
    j.deviceUnitZoneID,
    j.battery,
    j.temperature,
    j.soilTemperature,
    j.humidity,
    j.moisture,
    j.light,
    j.co2,
    j.tvoc,
    j.barometer,
    j.liquidPH,
    j.rainlevel,
    j.waterLevel,
    j.wind,
    j.dateCreated
  FROM JSON_TABLE(
    jsonData, '$[*]' COLUMNS(
	  deviceID INT PATH '$.deviceID',
	  tenantID INT PATH '$.tenantID',
	  deviceUnitID INT PATH '$.deviceUnitID',
	  deviceUnitZoneID INT PATH '$.deviceUnitZoneID',
      battery TINYINT PATH '$.battery',
      temperature DOUBLE PATH '$.temperature',
      soilTemperature DOUBLE PATH '$.soilTemperature',
      humidity DOUBLE PATH '$.humidity',
      moisture INT PATH '$.moisture',
      light TINYINT PATH '$.light',
      co2 INT PATH '$.co2',
      tvoc INT PATH '$.tvoc',
      barometer DOUBLE PATH '$.barometer',
      liquidPH DOUBLE PATH '$.liquidPH',
      rainlevel INT PATH '$.rainLevel',
      waterLevel TINYINT PATH '$.waterLevel',
      wind SMALLINT PATH '$.wind',
      dateCreated DATETIME PATH '$.dateCreated'
    )
  ) AS j;

  -- INSERT INTO EVENTDEVICE
END
""";

        private const string P_SensorDataReportBuilder =
"""
CREATE OR REPLACE PROCEDURE `SensorDataReportBuilder`(
    tenantID int,
	deviceID int,
    timeRange int,
    timeMDMY int,
    OUT sensorDataOut LONGTEXT
)
BEGIN
DECLARE sensorDataResult LONGTEXT;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- nolock read, dirty read for sensor
START TRANSACTION;
-- 0 minute, 1 hour, 2 day, 3 year, add interval 1+
CASE timeMDMY

-- MINUTE RANGE
	WHEN 0 THEN
SELECT JSON_OBJECT("sensorData",JSON_ARRAYAGG(records)) as sensorData
INTO sensorDataResult
FROM -- select with
(
SELECT
	DATE_FORMAT(DateCreated,'%Y-%m-%d %H %i') as dateResult,
	-- Traditional aggregates return a single value.
		JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
		)
    AS records
FROM
	sensorData
	WHERE DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange MINUTE AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID
	AND sensorData.Co2 < 8000 -- solving error data from CSS811 Co2
GROUP BY
	dateResult
) AS sensorDataResult;

-- DAY RANGE
    WHEN 1 THEN
SELECT JSON_OBJECT("sensorData",JSON_ARRAYAGG(records)) as sensorData
INTO sensorDataResult
FROM -- select with
(
SELECT
	DATE_FORMAT(DateCreated,'%Y-%m-%d %H') as dateResult,
	-- Traditional aggregates return a single value.
		JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
		)
    AS records
FROM
	sensorData
	WHERE DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange DAY AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID
	AND sensorData.Co2 < 8000 -- solving error data from CSS811 Co2
GROUP BY
	dateResult
) AS sensorDataResult;


-- MONTH RANGE
    WHEN 2 THEN
SELECT JSON_OBJECT("sensorData",JSON_ARRAYAGG(records)) as sensorData
INTO sensorDataResult
FROM -- select with
(
SELECT
	DATE_FORMAT(DateCreated,'%Y-%m-%d') as dateResult,
	-- Traditional aggregates return a single value.
		JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
		)
    AS records
FROM
	sensorData
	WHERE DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange MONTH AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID
	AND sensorData.Co2 < 8000 -- solving error data from CSS811 Co2
GROUP BY
	dateResult
) AS sensorDataResult;

-- YEAR RANGE
	WHEN 3 THEN
SELECT JSON_OBJECT("sensorData",JSON_ARRAYAGG(records)) as sensorData
INTO sensorDataResult
FROM -- select with
(
SELECT
	DATE_FORMAT(DateCreated,'%Y-%m-%d') as dateResult,
	-- Traditional aggregates return a single value.
		JSON_OBJECT(
			"battery", Battery,
			"temperature", Temperature,
			"soilTemperature", SoilTemperature,
			"humidity", Humidity,
			"moisture", Moisture,
			"light", Light,
			"co2", Co2,
			"tvoc", Tvoc,
			"barometer", Barometer,
			"liquidPH", LiquidPH,
			"rainLevel", RainLevel,
			"waterLevel", WaterLevel,
			"wind", Wind,
            "dateCreated",DateCreated
		)
    AS records
FROM
	sensorData
	WHERE DateCreated > CURRENT_TIMESTAMP - INTERVAL timeRange YEAR AND sensorData.DeviceID = deviceID and sensorData.TenantID = tenantID
	AND sensorData.Co2 < 8000 -- solving error data from CSS811 Co2
GROUP BY
	dateResult
) AS sensorDataResult;
        ELSE
        select null as sensorData  from sensorData LIMIT 0;
END CASE;

SET sensorDataOut := sensorDataResult;
COMMIT;
END
""";

        private const string P_SensorDataReportGet =
"""
CREATE OR REPLACE PROCEDURE `SensorDataReportGet`(
	tenantID int,
	getData int,
    deviceID int,
    reportID int
)
BEGIN
-- sensorDataReport has no TenantID column of its own, only DeviceID - join to device to scope
-- results to the caller's tenant.
CASE
	WHEN getData = 0 THEN
		select sensorDataReport.IDSensorDataReport, sensorDataReport.DeviceID, sensorDataReport.ReportName, sensorDataReport.DateGenerated
        from sensorDataReport
        join device on device.IDDevice = sensorDataReport.DeviceID
        where sensorDataReport.DeviceID = deviceID and device.TenantID = tenantID;
    WHEN getData > 0 THEN
		select sensorDataReport.*
        from sensorDataReport
        join device on device.IDDevice = sensorDataReport.DeviceID
        where sensorDataReport.IDSensorDataReport = reportID and device.TenantID = tenantID;
END CASE;
END
""";

        // ---------------------------------------------------------------------
        // TRIGGERS
        // ---------------------------------------------------------------------

        private const string TR_SensorDataSetDateTimeOnNull =
"""
CREATE OR REPLACE TRIGGER `sensorData_SetDateTimeOnNull`
BEFORE INSERT ON `sensorData`
FOR EACH ROW
BEGIN
    IF NEW.DateCreated IS NULL THEN
	SET NEW.DateCreated = CURRENT_TIMESTAMP();
    END IF;
END
""";

        // ---------------------------------------------------------------------
        // Public surface
        // ---------------------------------------------------------------------

        /// <summary>CREATE TABLE statements in a foreign-key safe creation order.</summary>
        public static readonly IReadOnlyList<string> Tables = new[]
        {
            T_Tenant,
            T_DeviceUnitZone,
            T_DeviceUnit,
            T_DeviceType,
            T_DeviceTypeRelay,
            T_DeviceTypeSensor,
            T_DeviceTypeService,
            T_DeviceConfigController,
            T_DeviceConfigSensor,
            T_UserRoleScope,
            T_UserRole,
            T_UserGroup,
            T_User,
            T_Device,
            T_SensorData,
            T_SensorDataReport,
            T_DeviceFirmware,
            T_EventDevice,
            T_EventService,
            T_ServerConfig,
        };

        /// <summary>CREATE OR REPLACE PROCEDURE statements.</summary>
        public static readonly IReadOnlyList<string> Procedures = new[]
        {
            P_ServerConfig,
            P_ServerConfigGet,
            P_ServerConfigAdd,
            P_TenantGet,
            P_TenantAdd,
            P_UserAdd,
            P_UserDelete,
            P_UserGet,
            P_UsersGet,
            P_UserUpdate,
            P_UserSetPassword,
            P_UserSecretGet,
            P_UserRoleGet,
            P_UserGroupAdd,
            P_UserGroupDelete,
            P_UserGroupGet,
            P_UserGroupsGet,
            P_DeviceAdd,
            P_DeviceCheckMacAddress,
            P_DeviceConfigControllerGet,
            P_DeviceConfigControllerUpdate,
            P_DeviceConfigSensorGet,
            P_DeviceConfigSensorUpdate,
            P_DeviceGetByDeviceConfigSensorId,
            P_DeviceGetByDeviceConfigControllerId,
            P_DeviceDelete,
            P_DeviceGet,
            P_DeviceGetById,
            P_DevicesGet,
            P_DeviceTypeGet,
            P_DeviceTypeRelayGet,
            P_DeviceTypeSensorGet,
            P_DeviceTypeServiceGet,
            P_DeviceUpdate,
            P_SensorDataDelete,
            P_SensorDataGet,
            P_SensorDataGetJson,
            P_SensorDataPush,
            P_SensorDataReportBuilder,
            P_SensorDataReportGet,
        };

        /// <summary>CREATE OR REPLACE TRIGGER statements.</summary>
        public static readonly IReadOnlyList<string> Triggers = new[]
        {
            TR_SensorDataSetDateTimeOnNull,
        };

        /// <summary>Every schema object, in the order it should be applied to an empty database.</summary>
        public static IEnumerable<string> AllObjects =>
            Tables.Concat(Procedures).Concat(Triggers);
    }
}
