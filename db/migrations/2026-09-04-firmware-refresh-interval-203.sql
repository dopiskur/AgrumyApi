-- Roadmap #203: automatic periodic firmware catalog refresh, configurable interval in Server Settings.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every statement uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  -- Null/0 = disabled (manual-only, pre-#203 behavior). Live-editable without an app restart -
  -- see FirmwareCatalogRefreshEvaluator's remarks, same pattern as WeatherPollIntervalMinutes.
  ADD COLUMN IF NOT EXISTS `FirmwareRefreshIntervalHours` INT DEFAULT NULL,
  -- Computed by FirmwareCatalogRefreshEvaluator only (api.Dal.EfRepository.ServerConfigFirmwareRefreshStateSetAsync) -
  -- never written by the admin Server Settings form.
  ADD COLUMN IF NOT EXISTS `FirmwareLastRefreshedAtUtc` DATETIME DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Firmware%';
