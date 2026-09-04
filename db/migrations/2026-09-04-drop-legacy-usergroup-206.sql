-- Roadmap #206: Phase 2 of the #204 RBAC consolidation - removes the legacy single-group
-- model now that every account has a real userUserRole row (Phase 1's backfill migration
-- confirmed this on invent.hr before this file was written) and no code path reads
-- user.UserGroupID or the userGroup table any more.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- NOT SAFE TO RE-RUN BLINDLY: DROP COLUMN/DROP TABLE fail loudly (not silently) if already
-- applied - that is the desired behavior here, not an accident. Run once.

ALTER TABLE `user` DROP FOREIGN KEY `fk_userGroup`;
ALTER TABLE `user` DROP COLUMN `UserGroupID`;
DROP TABLE `userGroup`;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'UserGroupID';   -- expect empty
--   SHOW TABLES LIKE 'userGroup';                  -- expect empty
