-- EfRepository.RevokeUserTokensAsync/ToDto read/write TokensValidAfterUtc unconditionally, so every
-- User query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `user`
  ADD COLUMN IF NOT EXISTS `TokensValidAfterUtc` DATETIME NULL DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'TokensValidAfterUtc';
