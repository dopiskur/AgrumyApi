-- Roadmap #180: CommandQueueService.IssueCommandAsync's dedup was check-then-insert
-- (HasActiveCommandAsync then AddCommandAsync) with no DB-level guarantee - two concurrent issue
-- requests for the same (DeviceID, ActionType) could both pass the check before either inserted,
-- creating two active commands for the same device/action.
--
-- Fix: a new ActiveKey column that mirrors ActionType while Status is Pending(0)/Acknowledged(1)
-- and is NULL once the row is terminal (Executed/Expired) - see DeviceCommandRow.ActiveKey and
-- EfRepository.Commands.cs (AddCommandAsync/SetCommandStatusAsync). A plain UNIQUE index on
-- (DeviceID, ActiveKey) then does the actual enforcement: both MySQL and PostgreSQL allow
-- unlimited NULLs through a unique index, so terminal rows never collide, but two concurrent
-- inserts racing for the same active (DeviceID, ActiveKey) pair cannot both succeed - the loser
-- gets a duplicate-key error, which EfRepository.AddCommandAsync now catches and turns into a
-- normal "this device already has one" outcome instead of a 500.
--
-- WHY THIS IS MANUAL: see 2026-08-31-devicepin-hardening.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `deviceCommand` table.
--
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS; the index-add is guarded the same way as
-- 2026-09-01-device-macaddress-tenant-unique.sql's MacAddress_TenantID_UNIQUE.
--
-- WILL FAIL LOUDLY (duplicate-key error) IF AN EXISTING (DeviceID, ActionType) PAIR ALREADY HAS
-- MORE THAN ONE Pending/Acknowledged ROW - run the pre-check below first and resolve any hits
-- (e.g. manually mark the stale duplicate Expired) before applying.

-- Pre-check (run manually, review output before proceeding):
--   SELECT DeviceID, ActionType, COUNT(*) c FROM `deviceCommand`
--   WHERE Status IN (0, 1) GROUP BY DeviceID, ActionType HAVING c > 1;

ALTER TABLE `deviceCommand`
  ADD COLUMN IF NOT EXISTS `ActiveKey` INT NULL;

-- Backfill: existing active rows get ActiveKey = ActionType (matching what AddCommandAsync now
-- sets on insert); terminal rows stay NULL, same as SetCommandStatusAsync leaves them.
UPDATE `deviceCommand` SET `ActiveKey` = `ActionType` WHERE `Status` IN (0, 1) AND `ActiveKey` IS NULL;

SET @index_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'deviceCommand' AND index_name = 'ux_deviceCommand_device_activekey'
);
SET @add_index_sql = IF(@index_exists = 0,
  'ALTER TABLE `deviceCommand` ADD UNIQUE INDEX `ux_deviceCommand_device_activekey` (`DeviceID`, `ActiveKey`)',
  'SELECT 1');
PREPARE stmt FROM @add_index_sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Sanity check after running:
--   SHOW INDEX FROM `deviceCommand` WHERE Key_name = 'ux_deviceCommand_device_activekey';
--   SELECT COUNT(*) FROM `deviceCommand` WHERE Status IN (0,1) AND ActiveKey IS NULL;  -- expect 0
