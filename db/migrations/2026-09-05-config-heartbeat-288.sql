-- EfRepository.ServerConfigGetAsync/UpdateAsync and DeviceMarkConfigSentAsync read/write
-- serverConfig.ConfigHeartbeatHours and device.LastFullConfigSentAt unconditionally, so every
-- ServerConfig/device query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `ConfigHeartbeatHours` INT NOT NULL DEFAULT 24;

ALTER TABLE `device`
  ADD COLUMN IF NOT EXISTS `LastFullConfigSentAt` DATETIME NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'ConfigHeartbeatHours';
--   SHOW COLUMNS FROM `device` LIKE 'LastFullConfigSentAt';
