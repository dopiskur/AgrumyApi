-- Roadmap #309: Relay1..Relay8 fixed columns on deviceConfigController replaced by one row per
-- ASSIGNED slot in a new deviceConfigControllerRelay table - no fixed slot-count ceiling baked
-- into the schema itself anymore (a bigger relay bank just means bigger Slot numbers, no new
-- column). Confirmed via SHOW CREATE TABLE before writing this.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- NOT SAFE TO RE-RUN: data migration + column drop is inherently one-shot, same rationale as
-- 2026-09-02-devicecontroller-intervallength-rename.sql. The CREATE TABLE alone is IF NOT EXISTS
-- for partial-failure safety, but re-running the whole file after a successful run fails loudly
-- at the DROP COLUMN step (columns already gone) rather than doing anything harmful.

CREATE TABLE IF NOT EXISTS `deviceConfigControllerRelay` (
  `IDDeviceConfigController` INT NOT NULL,
  `Slot` INT NOT NULL,
  `RelayFunction` INT NOT NULL,
  PRIMARY KEY (`IDDeviceConfigController`, `Slot`),
  CONSTRAINT `fk_deviceConfigControllerRelay_controller` FOREIGN KEY (`IDDeviceConfigController`) REFERENCES `deviceConfigController` (`IDDeviceConfigController`) ON DELETE CASCADE,
  CONSTRAINT `fk_deviceConfigControllerRelay_relayFunction` FOREIGN KEY (`RelayFunction`) REFERENCES `deviceTypeRelay` (`IDDeviceTypeRelay`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Only ASSIGNED slots become rows (RelayFunction 0/Disabled or NULL is simply omitted, matching
-- the new sparse model) - one INSERT per legacy column.
INSERT INTO `deviceConfigControllerRelay` (`IDDeviceConfigController`, `Slot`, `RelayFunction`)
SELECT `IDDeviceConfigController`, 1, `Relay1` FROM `deviceConfigController` WHERE `Relay1` IS NOT NULL AND `Relay1` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 2, `Relay2` FROM `deviceConfigController` WHERE `Relay2` IS NOT NULL AND `Relay2` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 3, `Relay3` FROM `deviceConfigController` WHERE `Relay3` IS NOT NULL AND `Relay3` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 4, `Relay4` FROM `deviceConfigController` WHERE `Relay4` IS NOT NULL AND `Relay4` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 5, `Relay5` FROM `deviceConfigController` WHERE `Relay5` IS NOT NULL AND `Relay5` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 6, `Relay6` FROM `deviceConfigController` WHERE `Relay6` IS NOT NULL AND `Relay6` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 7, `Relay7` FROM `deviceConfigController` WHERE `Relay7` IS NOT NULL AND `Relay7` <> 0
UNION ALL
SELECT `IDDeviceConfigController`, 8, `Relay8` FROM `deviceConfigController` WHERE `Relay8` IS NOT NULL AND `Relay8` <> 0;

ALTER TABLE `deviceConfigController`
  DROP FOREIGN KEY `fk_deviceConfigController_relay1`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay2`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay3`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay4`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay5`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay6`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay7`,
  DROP FOREIGN KEY `fk_deviceConfigController_relay8`,
  DROP COLUMN `Relay1`,
  DROP COLUMN `Relay2`,
  DROP COLUMN `Relay3`,
  DROP COLUMN `Relay4`,
  DROP COLUMN `Relay5`,
  DROP COLUMN `Relay6`,
  DROP COLUMN `Relay7`,
  DROP COLUMN `Relay8`;

-- Sanity check after running:
--   SELECT * FROM `deviceConfigControllerRelay` ORDER BY IDDeviceConfigController, Slot;
--   SHOW COLUMNS FROM `deviceConfigController` LIKE 'Relay%';  -- expect 1 row: RelayEnabled only
