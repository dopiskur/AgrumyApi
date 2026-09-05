-- EfRepository.ServerConfigGetAsync/UpdateAsync read/write PasswordMinLength/PasswordRequireComplexity
-- unconditionally, so every ServerConfig query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `PasswordMinLength` INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `PasswordRequireComplexity` TINYINT(1) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Password%';
