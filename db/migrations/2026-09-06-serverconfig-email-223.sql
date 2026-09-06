-- EfRepository.ServerConfigGetAsync/UpdateAsync read/write EmailEnabled/EmailHost/EmailPort/
-- EmailUseStartTls/EmailUsername/EmailPassword/EmailFromAddress/EmailFromName unconditionally, so
-- every ServerConfig query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `EmailEnabled` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `EmailHost` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `EmailPort` INT NOT NULL DEFAULT 587,
  ADD COLUMN IF NOT EXISTS `EmailUseStartTls` TINYINT(1) NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS `EmailUsername` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `EmailPassword` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `EmailFromAddress` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `EmailFromName` VARCHAR(100) NOT NULL DEFAULT 'Agrumy';

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Email%';
