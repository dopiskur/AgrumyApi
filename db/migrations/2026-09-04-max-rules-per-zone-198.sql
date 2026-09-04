-- Follow-up to #194: MaxRulesPerZone moves from a hardcoded constant to an admin-configurable
-- ServerConfig field, still hard-capped at 32 (AgrumyFirmware's MAX_RULES) by
-- ServerConfigApiController.Update regardless of what's stored here.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `serverConfig` table.
--
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS; the UPDATE only touches rows still at NULL.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `MaxRulesPerZone` INT DEFAULT NULL;

UPDATE `serverConfig`
SET `MaxRulesPerZone` = COALESCE(`MaxRulesPerZone`, 10)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SELECT MaxRulesPerZone FROM `serverConfig` WHERE IDServerConfig = 1;  -- expect 10 (or an admin-set value)
