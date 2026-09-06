-- Roadmap #234: tank/water-level management as a core feature (calibration, fill %/volume,
-- low-tank refill alert). Adds per-zone calibration (deviceUnitZone) and a global refill
-- threshold/hysteresis pair (serverConfig), same shape as the existing BatteryLowThreshold/
-- BatteryLowHysteresis alert.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only creates
-- tables in a brand-new (zero-table) database, never adds a column to one that already exists.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS.

ALTER TABLE `deviceUnitZone`
  ADD COLUMN IF NOT EXISTS `TankCapacityLiters` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WaterLevelRawEmpty` INT(11) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WaterLevelRawFull` INT(11) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `TankRefillNotifiedAt` DATETIME(6) DEFAULT NULL;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `TankRefillThreshold` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `TankRefillHysteresis` DOUBLE DEFAULT NULL;

-- Backfill row 1's threshold/hysteresis if it already existed before this migration - same
-- pattern as 2026-08-30-hysteresis-columns.sql, keeps TankRefillAlertEvaluator's defaults
-- visible in the admin UI instead of showing blank until someone opens Alerts and re-saves.
UPDATE `serverConfig`
SET `TankRefillThreshold` = COALESCE(`TankRefillThreshold`, 20.0),
    `TankRefillHysteresis` = COALESCE(`TankRefillHysteresis`, 5.0)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceUnitZone` LIKE 'Tank%';
--   SHOW COLUMNS FROM `deviceUnitZone` LIKE 'WaterLevelRaw%';
--   SELECT TankRefillThreshold, TankRefillHysteresis FROM serverConfig WHERE IDServerConfig = 1;  -- expect 20, 5 (or an admin-set value)
