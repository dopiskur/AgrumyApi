-- Acknowledgeable non-critical problem alerts on the Unit/Zone dashboard: an admin can dismiss a
-- crash/auth/sync/OTA-failure event so it stops keeping its Unit/Zone Orange before the fixed
-- expiry window elapses. The window itself becomes a ServerConfig setting (was hardcoded 24h in
-- EfRepository.DeviceUnits.cs), with an enable/disable toggle for the whole feature.
--
-- SAFE TO RE-RUN: ALTER TABLE uses IF NOT EXISTS; the INSERT is WHERE NOT EXISTS.

ALTER TABLE `eventDevice`
  ADD COLUMN IF NOT EXISTS `AcknowledgedAt` DATETIME DEFAULT NULL;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `ProblemEventAlertsEnabled` TINYINT(1) NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS `ProblemEventExpiryHours` INT NOT NULL DEFAULT 24;

-- Seed the single serverConfig row (id 1) if it doesn't exist yet - same rationale as the earlier
-- serverConfig migrations (e.g. 2026-09-03-sensordata-retention-15.sql).
INSERT INTO `serverConfig` (`IDServerConfig`, `ServerConfigName`, `ConfigKey`, `PortHTTP`, `PortHTTPS`)
SELECT 1, 'DefaultGenerated1', UUID(), 80, 443
WHERE NOT EXISTS (SELECT 1 FROM `serverConfig` WHERE `IDServerConfig` = 1);

-- No backfill UPDATE needed: MySQL/MariaDB's ALTER TABLE ... ADD COLUMN ... DEFAULT already fills
-- existing rows with the default, unlike the NULL-default columns other migrations here add.

-- Sanity check after running:
--   SHOW COLUMNS FROM `eventDevice` LIKE 'AcknowledgedAt';
--   SHOW COLUMNS FROM `serverConfig` LIKE 'ProblemEvent%';
--   SELECT `ProblemEventAlertsEnabled`, `ProblemEventExpiryHours` FROM `serverConfig` WHERE `IDServerConfig` = 1;
