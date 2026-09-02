-- Roadmap #114: "Lenght" -> "Length" hard cutover (user decision: alfa phase, no production, no
-- dual-support transition needed) - renames the four misspelled columns on `deviceConfigController`
-- to match the renamed C#/firmware property names. Confirmed via SHOW COLUMNS before writing this:
-- the live column names are literally VentilationIntervalLenght/LightIntervalLenght/
-- HeatingIntervalLenght/WaterPumpIntervalLenght (EF has no HasColumnName override, so the property
-- rename alone would otherwise orphan the old columns and read/write nulls from ones that don't exist).
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `deviceConfigController`
-- table. Run this by hand against each database that predates this change.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (old column name no longer exists) rather than
-- doing anything harmful - there is nothing to guard, a rename is inherently a one-shot operation.
ALTER TABLE `deviceConfigController`
  CHANGE COLUMN `VentilationIntervalLenght` `VentilationIntervalLength` int(11) NULL DEFAULT NULL,
  CHANGE COLUMN `LightIntervalLenght` `LightIntervalLength` int(11) NULL DEFAULT NULL,
  CHANGE COLUMN `HeatingIntervalLenght` `HeatingIntervalLength` int(11) NULL DEFAULT NULL,
  CHANGE COLUMN `WaterPumpIntervalLenght` `WaterPumpIntervalLength` int(11) NULL DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceConfigController` LIKE '%IntervalLength';  -- expect 4 rows
--   SHOW COLUMNS FROM `deviceConfigController` LIKE '%IntervalLenght';  -- expect 0 rows
