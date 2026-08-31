-- Roadmap #70: device-registration PIN hardening. DevicePin goes from a 4-digit INT to a
-- generated 6-char alphanumeric code (VARCHAR) with a 24h expiry, consumed by the first
-- successful registration (see api.Security.AuthenticationProvider.GetPin/VerifyPin and
-- DeviceApiController.DeviceRegistration).
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `user` table. Run
-- this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: MODIFY to the same type is a no-op, ADD COLUMN uses IF NOT EXISTS.

-- MySQL/MariaDB converts existing int values ("1234") to their string form in place. That is
-- fine: legacy 4-digit PINs keep their value but have DevicePinExpires = NULL below, which
-- VerifyPin deliberately treats as INVALID - the whole point of #70 is that the old 4-digit
-- space dies immediately. Every user generates a fresh PIN from My Profile when they next
-- register a device; nothing else about their account is affected.
ALTER TABLE `user` MODIFY `DevicePin` VARCHAR(8) NULL;

ALTER TABLE `user` ADD COLUMN IF NOT EXISTS `DevicePinExpires` DATETIME NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'DevicePin%';        -- varchar(8) + datetime
--   SELECT COUNT(*) FROM `user` WHERE DevicePinExpires IS NOT NULL;  -- expect 0 right after migration
