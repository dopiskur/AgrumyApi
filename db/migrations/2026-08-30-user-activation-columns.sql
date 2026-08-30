-- Roadmap #24/#63/#64/#68: email activation, tenant-scoped approval, self-service tenant
-- creation toggle, and the Enabled/EmailVerified login gate.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `user`/`serverConfig`
-- table. Run this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS; the UPDATE statements only touch rows
-- still at their pre-migration default, so re-running never clobbers a value set since.

ALTER TABLE `user`
  ADD COLUMN IF NOT EXISTS `EmailVerified` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `ActivationTokenHash` VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS `ActivationTokenExpiresAt` DATETIME NULL,
  ADD COLUMN IF NOT EXISTS `ActivationLastSentAt` DATETIME NULL;

-- Grandfather in every account that predates roadmap #24 - they never went through email
-- activation and must not be locked out of login by it (see roadmap #68's new EmailVerified
-- check in UserApiController.UserLogin/RefreshToken). Only a NEW self-registration is created
-- with EmailVerified=0 and actually needs to pass through GET /api/User/Activate.
UPDATE `user` SET `EmailVerified` = 1 WHERE `EmailVerified` = 0;

-- MariaDB/MySQL lack "ADD INDEX IF NOT EXISTS" support that also plays well with a fresh column
-- that may or may not have been added just above in the same run - check information_schema instead.
SET @index_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'user' AND index_name = 'ActivationTokenHash_UNIQUE'
);
SET @add_index_sql = IF(@index_exists = 0,
  'ALTER TABLE `user` ADD UNIQUE INDEX `ActivationTokenHash_UNIQUE` (`ActivationTokenHash`)',
  'SELECT 1');
PREPARE stmt FROM @add_index_sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `ActivationResendCooldownMinutes` INT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `AllowSelfServiceTenantCreation` TINYINT(1) DEFAULT NULL;

UPDATE `serverConfig`
SET `ActivationResendCooldownMinutes` = COALESCE(`ActivationResendCooldownMinutes`, 10),
    `AllowSelfServiceTenantCreation` = COALESCE(`AllowSelfServiceTenantCreation`, 0)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user`;
--   SELECT COUNT(*) FROM `user` WHERE EmailVerified = 0;                                 -- expect 0 (pre-existing rows all grandfathered)
--   SELECT ActivationResendCooldownMinutes, AllowSelfServiceTenantCreation FROM `serverConfig` WHERE IDServerConfig = 1; -- expect 10, 0 (or an admin-set value)
