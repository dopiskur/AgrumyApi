-- Roadmap #147: Agrumy.Relay device-forwarding infrastructure. ServerConfig gained
-- RelayEnabled/RelayMode/RelayWaitWindowSeconds but this file was never written when that
-- commit landed - EfRepository.ServerConfigGetAsync/UpdateAsync read/write these columns
-- unconditionally, so every ServerConfig query fails with "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `RelayEnabled` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `RelayMode` INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `RelayWaitWindowSeconds` INT NOT NULL DEFAULT 30;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Relay%';
