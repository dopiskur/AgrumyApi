-- Roadmap #115: multiple schedule windows per day, per relay function (completes #39's
-- single-window design). Replaces the 16 flat Ventilation/Light/Heating/WaterPump
-- Schedule{Enabled,DaysOfWeek,Start,Duration} columns on deviceConfigController with a real
-- one-to-many table - a relay function can now have any number of windows (e.g. 6:00-6:30,
-- 14:00-14:30, 20:00-20:15), OR'd together by the firmware (RelayLogic's computeScheduleState
-- stays single-window; a new computeAnyScheduleState ORs a list of them, called from
-- ActuatorController).
--
-- No separate "enabled" concept (confirmed design): a slot's mere presence in this table means
-- it is active - zero slots for a function means that function never turns on in schedule mode,
-- same "leave the pins alone" semantics the old disabled flag had.
--
-- RelayFunction matches deviceTypeRelay's seed IDs (1=Ventilation, 2=Light, 3=Heating,
-- 4=Water pump) and AgrumyDevice's RelayFunctionType enum - never renumber independently.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- NOT SAFE TO RE-RUN: the DROP COLUMN block at the end fails loudly on a second run (columns
-- already gone) - nothing here silently duplicates data on a retry after a real failure, since a
-- failed run either never reached the drop (columns still there for a clean re-run of the whole
-- script) or already dropped them (the whole script is done).

CREATE TABLE IF NOT EXISTS `deviceScheduleSlot` (
  `IDDeviceScheduleSlot` INT NOT NULL AUTO_INCREMENT,
  `DeviceConfigControllerID` INT NOT NULL,
  `RelayFunction` INT NOT NULL,
  `DaysOfWeek` INT NOT NULL DEFAULT 0,
  `Start` INT NOT NULL DEFAULT 0,
  `Duration` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`IDDeviceScheduleSlot`),
  KEY `fk_deviceScheduleSlot_deviceConfigController_idx` (`DeviceConfigControllerID`),
  CONSTRAINT `fk_deviceScheduleSlot_deviceConfigController` FOREIGN KEY (`DeviceConfigControllerID`) REFERENCES `deviceConfigController` (`IDDeviceConfigController`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Migrate any existing single-window schedule that was actually enabled into one slot row each,
-- before the old flat columns are dropped below. A disabled schedule's stale/garbage field
-- values are deliberately NOT migrated - they were never active.
INSERT INTO `deviceScheduleSlot` (`DeviceConfigControllerID`, `RelayFunction`, `DaysOfWeek`, `Start`, `Duration`)
SELECT `IDDeviceConfigController`, 1, `VentilationScheduleDaysOfWeek`, `VentilationScheduleStart`, `VentilationScheduleDuration`
FROM `deviceConfigController` WHERE `VentilationScheduleEnabled` = 1;
INSERT INTO `deviceScheduleSlot` (`DeviceConfigControllerID`, `RelayFunction`, `DaysOfWeek`, `Start`, `Duration`)
SELECT `IDDeviceConfigController`, 2, `LightScheduleDaysOfWeek`, `LightScheduleStart`, `LightScheduleDuration`
FROM `deviceConfigController` WHERE `LightScheduleEnabled` = 1;
INSERT INTO `deviceScheduleSlot` (`DeviceConfigControllerID`, `RelayFunction`, `DaysOfWeek`, `Start`, `Duration`)
SELECT `IDDeviceConfigController`, 3, `HeatingScheduleDaysOfWeek`, `HeatingScheduleStart`, `HeatingScheduleDuration`
FROM `deviceConfigController` WHERE `HeatingScheduleEnabled` = 1;
INSERT INTO `deviceScheduleSlot` (`DeviceConfigControllerID`, `RelayFunction`, `DaysOfWeek`, `Start`, `Duration`)
SELECT `IDDeviceConfigController`, 4, `WaterPumpScheduleDaysOfWeek`, `WaterPumpScheduleStart`, `WaterPumpScheduleDuration`
FROM `deviceConfigController` WHERE `WaterPumpScheduleEnabled` = 1;

-- Hard cutover (same treatment as #114 - alpha phase, no production): drop the now-superseded
-- flat columns.
ALTER TABLE `deviceConfigController`
  DROP COLUMN `VentilationScheduleEnabled`, DROP COLUMN `VentilationScheduleDaysOfWeek`, DROP COLUMN `VentilationScheduleStart`, DROP COLUMN `VentilationScheduleDuration`,
  DROP COLUMN `LightScheduleEnabled`, DROP COLUMN `LightScheduleDaysOfWeek`, DROP COLUMN `LightScheduleStart`, DROP COLUMN `LightScheduleDuration`,
  DROP COLUMN `HeatingScheduleEnabled`, DROP COLUMN `HeatingScheduleDaysOfWeek`, DROP COLUMN `HeatingScheduleStart`, DROP COLUMN `HeatingScheduleDuration`,
  DROP COLUMN `WaterPumpScheduleEnabled`, DROP COLUMN `WaterPumpScheduleDaysOfWeek`, DROP COLUMN `WaterPumpScheduleStart`, DROP COLUMN `WaterPumpScheduleDuration`;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceConfigController` LIKE '%Schedule%';  -- expect 0 rows
--   SELECT DeviceConfigControllerID, RelayFunction, COUNT(*) FROM `deviceScheduleSlot` GROUP BY 1, 2;
