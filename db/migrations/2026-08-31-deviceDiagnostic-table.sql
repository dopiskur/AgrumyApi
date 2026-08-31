-- Roadmap #7: device diagnostics / heartbeat - new `deviceDiagnostic` table.
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a table to a database that already
-- has others. Run this by hand against each such database before deploying the #7 code.
--
-- SAFE TO RE-RUN: guarded with IF NOT EXISTS.
--
-- One row per device (DeviceID is the PK, not AUTO_INCREMENT), upserted by the API on every
-- config poll (POST /api/Device/Config); LastSeenAt is the SERVER clock at the last poll.

CREATE TABLE IF NOT EXISTS `deviceDiagnostic` (
  `DeviceID` INT NOT NULL,
  `TenantID` INT DEFAULT NULL,
  `LastSeenAt` DATETIME(6) DEFAULT NULL,
  `UptimeSeconds` BIGINT DEFAULT NULL,
  `RssiDbm` INT DEFAULT NULL,
  `FreeHeapBytes` BIGINT DEFAULT NULL,
  `FirmwareVersion` VARCHAR(20) DEFAULT NULL,
  PRIMARY KEY (`DeviceID`),
  CONSTRAINT `FK_deviceDiagnostic_device_DeviceID` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceDiagnostic`;
