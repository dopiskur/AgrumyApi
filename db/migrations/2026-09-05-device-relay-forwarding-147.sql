-- Roadmap #147 (part 2): remaining schema gap from e800c6d, same missing-migration story as
-- 2026-09-05-serverconfig-relay-forwarding-147.sql - device.IsRelay/RelayProfile, the widened
-- MacAddress, and the new relayDeviceMapping table were never migrated either.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN/CREATE TABLE use IF NOT EXISTS; MODIFY COLUMN is idempotent (widening
-- an already-64-char column to 64 is a no-op).

ALTER TABLE `device`
  ADD COLUMN IF NOT EXISTS `IsRelay` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `RelayProfile` INT DEFAULT NULL,
  MODIFY COLUMN `MacAddress` VARCHAR(64) NULL;

CREATE TABLE IF NOT EXISTS `relayDeviceMapping` (
  `IDRelayDeviceMapping` INT NOT NULL AUTO_INCREMENT,
  `IDRelayDevice` INT NOT NULL,
  `DevEUI` VARCHAR(16) NOT NULL,
  `IDDevice` INT NOT NULL,
  `DateCreated` DATETIME DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`IDRelayDeviceMapping`),
  UNIQUE KEY `ux_relayDeviceMapping_relay_deveui` (`IDRelayDevice`, `DevEUI`),
  KEY `ix_relayDeviceMapping_IDDevice` (`IDDevice`),
  CONSTRAINT `fk_relayDeviceMapping_relay` FOREIGN KEY (`IDRelayDevice`) REFERENCES `device` (`IDDevice`),
  CONSTRAINT `fk_relayDeviceMapping_device` FOREIGN KEY (`IDDevice`) REFERENCES `device` (`IDDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW COLUMNS FROM `device` LIKE 'IsRelay'; SHOW COLUMNS FROM `device` LIKE 'RelayProfile';
--   SHOW COLUMNS FROM `device` LIKE 'MacAddress';   -- expect varchar(64)
--   SHOW CREATE TABLE `relayDeviceMapping`;
