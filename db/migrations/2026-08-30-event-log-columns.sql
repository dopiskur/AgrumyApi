-- Roadmap #28: device event log backend.
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a column to a table that already
-- exists (eventDevice/serverConfig both predate this change). Run this by hand against each
-- database that predates this change.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS; the serverConfig UPDATE only touches a
-- still-NULL value, so re-running never clobbers a value an admin already edited via the UI.

ALTER TABLE `eventDevice`
  ADD COLUMN IF NOT EXISTS `TenantID` INT NOT NULL DEFAULT 0;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `EventDedupeMinutes` INT DEFAULT NULL;

-- Backfill row 1's EventDedupeMinutes if it already existed before this migration (mirrors the
-- same pattern used for the hysteresis columns, 2026-08-30-hysteresis-columns.sql).
UPDATE `serverConfig`
SET `EventDedupeMinutes` = COALESCE(`EventDedupeMinutes`, 10)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SHOW COLUMNS FROM `eventDevice`;
--   SELECT EventDedupeMinutes FROM `serverConfig` WHERE IDServerConfig = 1;  -- expect 10 (or an admin-set value)
