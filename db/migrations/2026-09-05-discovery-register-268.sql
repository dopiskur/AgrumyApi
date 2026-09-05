-- Roadmap #268 (part 2): Register flow. tenantWifiConfig stores the WiFi AP(s) an admin can hand
-- to a newly discovered device instead of typing SSID/password in on every Register (0/1/many
-- branching lives in DiscoveryApiController.Register, not here). deviceCommand.Payload carries the
-- {Username, PIN, SSID, password} a ProvisionDevice command sends to the winning scanning device.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS `tenantWifiConfig` (
  `IDTenantWifiConfig` INT NOT NULL AUTO_INCREMENT,
  `TenantID` INT NOT NULL,
  `Ssid` VARCHAR(32) NOT NULL,
  `Password` VARCHAR(64) NOT NULL,
  `DateCreated` DATETIME DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`IDTenantWifiConfig`),
  KEY `ix_tenantWifiConfig_tenant` (`TenantID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `deviceCommand`
  ADD COLUMN IF NOT EXISTS `Payload` TEXT DEFAULT NULL;

-- Sanity check after running:
--   SHOW CREATE TABLE `tenantWifiConfig`;
--   SHOW COLUMNS FROM `deviceCommand` LIKE 'Payload';
