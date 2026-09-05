-- ScheduleTimeZone moved from the single global serverConfig row to per-tenant. EfRepository.Tenants
-- reads/writes tenant.ScheduleTimeZone unconditionally, so every Tenant query fails with "Unknown
-- column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS; the backfill UPDATE only touches rows still NULL.

ALTER TABLE `tenant`
  ADD COLUMN IF NOT EXISTS `ScheduleTimeZone` VARCHAR(64) NULL;

-- Carries the previous global default forward so no device silently regresses to UTC the moment
-- this deploys - every tenant starts out with whatever the server-wide value used to be. No-op if
-- that value was never set (both sides NULL).
UPDATE `tenant`
SET `ScheduleTimeZone` = (SELECT `ScheduleTimeZone` FROM `serverConfig` WHERE `IDServerConfig` = 1)
WHERE `ScheduleTimeZone` IS NULL;

-- The old serverConfig.ScheduleTimeZone column is deliberately left in place, unused - see
-- api.Dal.Entities.ServerConfigRow for why it's not dropped.

-- Sanity check after running:
--   SHOW COLUMNS FROM `tenant` LIKE 'ScheduleTimeZone';
--   SELECT IDTenant, TenantName, ScheduleTimeZone FROM `tenant`;
