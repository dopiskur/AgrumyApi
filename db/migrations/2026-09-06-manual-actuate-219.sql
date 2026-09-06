-- Roadmap #219: manual actuate (Heating/Ventilation/WaterPump), Duration + Target mode.
-- Generalizes deviceUnitZone's existing WaterPump-only MaxRunSeconds safety cap to the other two
-- manually-triggerable functions, and adds deviceManualOverride to track currently-active
-- manual commands (DeviceConfigBuilder reads these on each device's next config poll).
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only creates
-- tables in a brand-new (zero-table) database, never adds a column/table to one that already
-- exists.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS, CREATE TABLE uses IF NOT EXISTS.

ALTER TABLE `deviceUnitZone`
  ADD COLUMN IF NOT EXISTS `HeatingMaxRunSeconds` INT(11) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `VentilationMaxRunSeconds` INT(11) DEFAULT NULL;

CREATE TABLE IF NOT EXISTS `deviceManualOverride` (
  `IDDeviceManualOverride` INT(11) NOT NULL AUTO_INCREMENT,
  `DeviceID` INT(11) NOT NULL,
  `TenantID` INT(11) NOT NULL,
  `RelayFunction` INT(11) NOT NULL,
  `Mode` INT(11) NOT NULL,
  `StartedAtUtc` DATETIME(6) NOT NULL,
  `ExpiresAtUtc` DATETIME(6) NOT NULL,
  `TargetMetric` INT(11) DEFAULT NULL,
  `TargetThreshold` DOUBLE DEFAULT NULL,
  `TargetHysteresis` DOUBLE DEFAULT NULL,
  PRIMARY KEY (`IDDeviceManualOverride`),
  UNIQUE KEY `ux_deviceManualOverride_device_relayfunction` (`DeviceID`, `RelayFunction`),
  CONSTRAINT `FK_deviceManualOverride_device_DeviceID` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceUnitZone` LIKE '%MaxRunSeconds';   -- WaterPump/Heating/Ventilation, all three present
--   SHOW CREATE TABLE `deviceManualOverride`;                    -- unique (DeviceID, RelayFunction), FK to device present
