-- EfRepository.ServerConfigGetAsync/UpdateAsync read/write DevicePinValidMinutes
-- unconditionally, so every ServerConfig query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `DevicePinValidMinutes` INT NOT NULL DEFAULT 60;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'DevicePin%';
