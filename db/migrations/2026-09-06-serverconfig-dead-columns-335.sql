-- Roadmap #335: ServerConfigName/ConfigKey/ServerConfigCol are confirmed dead - populated on
-- creation but never read by any consumer outside the entity/DbContext itself. Removed from
-- api.Models.ServerConfig, ServerConfigRow, and EfRepository.ServerConfig.cs's mappings.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- NOT SAFE TO RE-RUN: DROP COLUMN is inherently one-shot, same rationale as
-- 2026-09-02-devicecontroller-intervallength-rename.sql - a second run fails loudly (columns
-- already gone) rather than doing anything harmful.

ALTER TABLE `serverConfig`
  DROP COLUMN `ServerConfigName`,
  DROP COLUMN `ConfigKey`,
  DROP COLUMN `serverConfigcol`;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'ServerConfigName';  -- expect 0 rows
--   SHOW COLUMNS FROM `serverConfig` LIKE 'ConfigKey';  -- expect 0 rows
--   SHOW COLUMNS FROM `serverConfig` LIKE 'serverConfigcol';  -- expect 0 rows
