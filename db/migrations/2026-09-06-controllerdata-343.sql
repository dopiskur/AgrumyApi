-- Roadmap #343: real-time relay on/off state, source of truth for both real and simulated
-- devices - see api.Dal.Entities.ControllerDataRow. One row per (DeviceID, RelayFunction) pair,
-- upserted on every actual relay transition, not an append-only log like sensorData.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS - a second run is a no-op.

CREATE TABLE IF NOT EXISTS `controllerData` (
  `IDControllerData` int(11) NOT NULL AUTO_INCREMENT,
  `DeviceID` int(11) NOT NULL,
  `TenantID` int(11) NOT NULL,
  `RelayFunction` int(11) NOT NULL,
  `IsOn` tinyint(1) NOT NULL,
  `DateChanged` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`IDControllerData`),
  UNIQUE KEY `ux_controllerData_device_relayFunction` (`DeviceID`, `RelayFunction`),
  CONSTRAINT `fk_controllerData_device` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `controllerData`;
