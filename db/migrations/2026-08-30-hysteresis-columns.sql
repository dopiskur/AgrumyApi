-- Hysteresis (dead-zone) margins for the threshold-based relay logic (roadmap #10).
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a column to a table that already
-- exists. Run this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS; the UPDATE only touches rows that are
-- still NULL, so re-running never clobbers a value an admin already edited via the UI.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `WaterLevelHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `TemperatureHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `HumidityHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `LightHysteresis` DOUBLE DEFAULT NULL;

ALTER TABLE `deviceConfigController`
  ADD COLUMN IF NOT EXISTS `WaterLevelHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `TemperatureHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `HumidityHysteresis` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `LightHysteresis` DOUBLE DEFAULT NULL;

-- Seed the single serverConfig row (id 1) if it doesn't exist yet - normally
-- ServerConfigGetAsync auto-creates it from appsettings.json on first read, but a device could
-- be added before that ever happens on an existing install.
INSERT INTO `serverConfig` (`IDServerConfig`, `ServerConfigName`, `ConfigKey`, `PortHTTP`, `PortHTTPS`,
                            `WaterLevelHysteresis`, `TemperatureHysteresis`, `HumidityHysteresis`, `LightHysteresis`)
SELECT 1, 'DefaultGenerated1', UUID(), 80, 443, 5.0, 1.0, 5.0, 20.0
WHERE NOT EXISTS (SELECT 1 FROM `serverConfig` WHERE `IDServerConfig` = 1);

-- Row 1 predates this migration on any install that already had a serverConfig row (invent.hr,
-- any provisioned dev DB) - the INSERT above only fires for a brand new row, so backfill the new
-- columns on the existing one too. Only touches still-NULL values, matching the
-- appsettings.json.example ServerConfig:Hysteresis defaults.
UPDATE `serverConfig`
SET `WaterLevelHysteresis`  = COALESCE(`WaterLevelHysteresis`,  5.0),
    `TemperatureHysteresis` = COALESCE(`TemperatureHysteresis`, 1.0),
    `HumidityHysteresis`    = COALESCE(`HumidityHysteresis`,    5.0),
    `LightHysteresis`       = COALESCE(`LightHysteresis`,       20.0)
WHERE `IDServerConfig` = 1;

-- Backfill existing devices' Controller config with the (now guaranteed to exist) serverConfig
-- defaults - only where still NULL, so a value already set some other way is left alone.
UPDATE `deviceConfigController` dcc
  JOIN `serverConfig` sc ON sc.`IDServerConfig` = 1
SET dcc.`WaterLevelHysteresis`  = COALESCE(dcc.`WaterLevelHysteresis`,  sc.`WaterLevelHysteresis`),
    dcc.`TemperatureHysteresis` = COALESCE(dcc.`TemperatureHysteresis`, sc.`TemperatureHysteresis`),
    dcc.`HumidityHysteresis`    = COALESCE(dcc.`HumidityHysteresis`,    sc.`HumidityHysteresis`),
    dcc.`LightHysteresis`       = COALESCE(dcc.`LightHysteresis`,       sc.`LightHysteresis`);

-- Sanity check after running:
--   SELECT * FROM `serverConfig` WHERE `IDServerConfig` = 1;
--   SELECT COUNT(*) FROM `deviceConfigController` WHERE `WaterLevelHysteresis` IS NULL;  -- expect 0
