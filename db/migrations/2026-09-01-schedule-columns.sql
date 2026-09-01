-- Roadmap #39: wall-clock schedule relay control - a third control mode alongside threshold and
-- interval, plus the server-wide timezone schedule windows are evaluated against.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only provisions a
-- brand-new (zero-table) database, never alters an existing `deviceConfigController`/`serverConfig`
-- table. Run this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS.

ALTER TABLE `deviceConfigController`
  ADD COLUMN IF NOT EXISTS `VentilationScheduleEnabled` TINYINT(1) DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `VentilationScheduleDaysOfWeek` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `VentilationScheduleStart` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `VentilationScheduleDuration` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `LightScheduleEnabled` TINYINT(1) DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `LightScheduleDaysOfWeek` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `LightScheduleStart` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `LightScheduleDuration` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `HeatingScheduleEnabled` TINYINT(1) DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `HeatingScheduleDaysOfWeek` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `HeatingScheduleStart` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `HeatingScheduleDuration` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `WaterPumpScheduleEnabled` TINYINT(1) DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `WaterPumpScheduleDaysOfWeek` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `WaterPumpScheduleStart` INT DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `WaterPumpScheduleDuration` INT DEFAULT 0;

-- Install-wide IANA zone id (e.g. "Europe/Zagreb") the Start/Duration windows above are evaluated
-- against - see api.Models.ServerConfig.ScheduleTimeZone for why this is one zone for the whole
-- install rather than per-device/tenant. NULL (not yet configured) is a valid, safe state:
-- TimeZoneHelper.GetUtcOffsetSeconds treats it as UTC, so schedule mode is inert, not broken, until
-- an admin sets this on the Server Settings page.
ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `ScheduleTimeZone` VARCHAR(64) DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceConfigController` LIKE '%Schedule%';   -- expect 16 rows
--   SHOW COLUMNS FROM `serverConfig` LIKE 'ScheduleTimeZone';       -- expect 1 row, varchar(64)
