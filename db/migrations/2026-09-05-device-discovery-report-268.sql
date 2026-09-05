-- Roadmap #268 (part 1): "Scan for new devices" - deviceDiscoveryReport stores each scanning
-- device's sighting of a nearby Agrumy_ AP (AgrumyFirmware's ScanForDevices command handler lands
-- in a later step). Aggregation/best-Rssi-pick logic lives in EfRepository, not this table.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS `deviceDiscoveryReport` (
  `IDReport` INT NOT NULL AUTO_INCREMENT,
  `ScanningDeviceID` INT NOT NULL,
  `DiscoveredApMac` VARCHAR(64) NOT NULL,
  `Rssi` INT DEFAULT NULL,
  `DateReported` DATETIME(6) NOT NULL,
  PRIMARY KEY (`IDReport`),
  KEY `ix_deviceDiscoveryReport_apMac` (`DiscoveredApMac`),
  CONSTRAINT `fk_deviceDiscoveryReport_device` FOREIGN KEY (`ScanningDeviceID`) REFERENCES `device` (`IDDevice`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceDiscoveryReport`;
