-- Roadmap #179: BootstrapSetPassword had no gate beyond rate limiting - any anonymous visitor who
-- found the endpoint before the real admin could claim the Global Admin account on a freshly
-- deployed instance. EfRepository.SeedBootstrapAdminAsync now generates a random one-time setup
-- secret alongside the bootstrap admin row, hashes it into these two new columns, and logs the
-- plaintext once at startup; BootstrapAdminSetPasswordAsync requires it to match before it will
-- ever touch PwdHash/PwdSalt.
--
-- WHY THIS IS MANUAL: see 2026-08-31-devicepin-hardening.sql - EnsureSchemaAsync() only provisions
-- a brand-new (zero-table) database, never alters an existing `user` table.
--
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.
--
-- NOTE: on any database where the bootstrap admin has already had a password set (PwdHash NOT
-- NULL), these columns stay NULL forever and BootstrapAdminPendingAsync is already false - this
-- migration has no functional effect there beyond adding the (unused) columns.

ALTER TABLE `user`
  ADD COLUMN IF NOT EXISTS `BootstrapSecretHash` TEXT NULL,
  ADD COLUMN IF NOT EXISTS `BootstrapSecretSalt` VARCHAR(128) NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'BootstrapSecret%';   -- both nullable
