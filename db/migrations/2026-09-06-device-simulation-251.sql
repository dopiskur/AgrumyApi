-- Roadmap #251 modality A: per-metric sensor-reading overrides for an existing, physical device
-- ("Simulation Mode" toggle + sliders on the Web Device Simulation page). One row per device,
-- 1:1 PK-is-the-FK shape, same as deviceDiagnostic.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one or seeds new
-- reference rows into one that already has data.
--
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS; the role INSERT is guarded by a NOT EXISTS check.

CREATE TABLE IF NOT EXISTS `deviceSimulation` (
  `DeviceID` int(11) NOT NULL,
  `Enabled` tinyint(1) NOT NULL,
  `Temperature` double DEFAULT NULL,
  `SoilTemperature` double DEFAULT NULL,
  `Humidity` double DEFAULT NULL,
  `Battery` int(11) DEFAULT NULL,
  `Moisture` int(11) DEFAULT NULL,
  `Light` int(11) DEFAULT NULL,
  `Co2` int(11) DEFAULT NULL,
  `Tvoc` int(11) DEFAULT NULL,
  `Barometer` double DEFAULT NULL,
  `LiquidPH` double DEFAULT NULL,
  `RainLevel` int(11) DEFAULT NULL,
  `WaterLevel` int(11) DEFAULT NULL,
  `Wind` int(11) DEFAULT NULL,
  PRIMARY KEY (`DeviceID`),
  CONSTRAINT `FK_deviceSimulation_device_DeviceID` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- New composable role (api.Security.RoleNames.SimulationAdministrator) - tenant-scoped, same
-- scope as TenantAdmin/TenantUser, looked up by name everywhere in code so its generated
-- IDUserRole value doesn't matter.
INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Simulation administrator', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'tenant')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Simulation administrator');

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceSimulation`;
--   SELECT RoleName, RoleScopeID FROM `userRole` WHERE RoleName = 'Simulation administrator';
