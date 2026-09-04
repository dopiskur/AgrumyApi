-- Roadmap #253: Tenant export/import (migrating a tenant between Agrumy servers). An imported
-- user's password hash is portable (same PBKDF2 parameters everywhere) but nobody on the target
-- server has proven they still know it, so login is gated until they do - see
-- UserApiController.UserLogin's 428 response and the new ForceChangePassword endpoint.
--
-- WHY THIS IS MANUAL: see 2026-08-31-devicepin-hardening.sql - EnsureSchemaAsync() only provisions
-- a brand-new (zero-table) database, never alters an existing `user` table.
--
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS. Every existing row defaults to 0 (false), which
-- is correct - only rows created by TenantImportService going forward should ever be 1.

ALTER TABLE `user`
  ADD COLUMN IF NOT EXISTS `MustChangePassword` TINYINT(1) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'MustChangePassword';   -- NOT NULL, DEFAULT 0
