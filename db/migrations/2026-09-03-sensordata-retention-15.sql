-- Roadmap #15: automatic sensorData retention. ServerConfig.SensorDataRetentionDays feeds both
-- the PostgreSQL/TimescaleDB add_retention_policy() call (EfRepository.ApplyRetentionPolicyAsync)
-- and the MariaDB/MySQL SensorDataRetentionBackgroundService daily purge job - one shared column,
-- two different enforcement mechanisms underneath (#14's tiered-hybrid split).
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ALTER TABLE uses IF NOT EXISTS; the INSERT is WHERE NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `SensorDataRetentionDays` INT DEFAULT NULL;

-- Seed the single serverConfig row (id 1) if it doesn't exist yet - same rationale as the
-- earlier serverConfig migrations (e.g. 2026-09-02-battery-monitoring-12.sql).
INSERT INTO `serverConfig` (`IDServerConfig`, `ServerConfigName`, `ConfigKey`, `PortHTTP`, `PortHTTPS`)
SELECT 1, 'DefaultGenerated1', UUID(), 80, 443
WHERE NOT EXISTS (SELECT 1 FROM `serverConfig` WHERE `IDServerConfig` = 1);

-- No backfill UPDATE: unlike the battery/hysteresis fields, NULL ("no automatic retention
-- configured yet") IS the deliberate default here, same reasoning as ScheduleTimeZone - an admin
-- must opt in with a real number.

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'SensorDataRetentionDays';
--   SELECT `SensorDataRetentionDays` FROM `serverConfig` WHERE `IDServerConfig` = 1;
