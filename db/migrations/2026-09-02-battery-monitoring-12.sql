-- Roadmap #12: battery voltage/percentage monitoring with a low-battery alarm - supports both
-- VoltageDivider (analog, existing "Analog voltage" deviceTypeSensor row 2001) and MAX17048 (new
-- I2C fuel gauge row 1009, RECOMMENDED, coulomb counting) as SensorBattery options, plus the
-- VoltageDivider's R1/R2 calibration and the low-battery alert's threshold/hysteresis.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every ALTER TABLE/INSERT uses IF NOT EXISTS / WHERE NOT EXISTS; the UPDATE only
-- touches rows that are still NULL, so re-running never clobbers a value an admin already edited.

-- New MAX17048 option in the same deviceTypeSensor-backed SensorBattery dropdown that already
-- offers 2001 "Analog voltage" (roadmap #91 pattern) - id 1009 keeps it in the existing "1xxx =
-- digital/I2C sensor" numbering family (after BH1750=1008), ahead of the "2xxx = analog sensor"
-- family, so it sorts first in the dropdown - matching the roadmap's "MAX17048 first/recommended"
-- decision without needing any explicit ordering logic in the Web layer.
INSERT INTO `deviceTypeSensor` (`IDDeviceTypeSensor`, `SensorName`, `SensorDescription`, `Battery`,
                                 `Temperature`, `TemperatureSoil`, `Humidity`, `Moisture`, `Light`,
                                 `Co2`, `Tvoc`, `Barometer`, `WaterPH`, `WaterTankLevel`, `RainLevel`, `Wind`)
SELECT 1009, 'MAX17048', 'I2C fuel gauge (coulomb counting), address 0x36 - recommended, more precise than a voltage divider', 1,
       0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM `deviceTypeSensor` WHERE `IDDeviceTypeSensor` = 1009);

-- VoltageDivider calibration - the ACTUAL resistors wired (ohms), only meaningful when
-- SensorBattery=2001. No backfill: null means "not configured yet", same as an unset threshold.
ALTER TABLE `deviceConfigSensor`
  ADD COLUMN IF NOT EXISTS `BatteryDividerR1` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `BatteryDividerR2` DOUBLE DEFAULT NULL;

-- Low-battery alert threshold/hysteresis (percent) - global (ServerConfig only, no per-device
-- DeviceConfigController override, unlike the #10 relay hysteresis fields) since the alert runs
-- server-side (LowBatteryAlertEvaluator, #40 pattern), not on-device relay logic.
ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `BatteryLowThreshold` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `BatteryLowHysteresis` DOUBLE DEFAULT NULL;

-- Dedup marker for the low-battery alert streak - same shape as deviceDiagnostic.OfflineNotifiedAt (#40).
ALTER TABLE `deviceDiagnostic`
  ADD COLUMN IF NOT EXISTS `LowBatteryNotifiedAt` DATETIME(6) DEFAULT NULL;

-- Seed the single serverConfig row (id 1) if it doesn't exist yet - same rationale as the
-- hysteresis migration's seed INSERT.
INSERT INTO `serverConfig` (`IDServerConfig`, `ServerConfigName`, `ConfigKey`, `PortHTTP`, `PortHTTPS`,
                            `BatteryLowThreshold`, `BatteryLowHysteresis`)
SELECT 1, 'DefaultGenerated1', UUID(), 80, 443, 20.0, 5.0
WHERE NOT EXISTS (SELECT 1 FROM `serverConfig` WHERE `IDServerConfig` = 1);

-- Row 1 predates this migration on any install that already had a serverConfig row - backfill the
-- new columns on the existing one too, matching AgrumySettings' BatteryLowThreshold/Hysteresis defaults.
UPDATE `serverConfig`
SET `BatteryLowThreshold`  = COALESCE(`BatteryLowThreshold`,  20.0),
    `BatteryLowHysteresis` = COALESCE(`BatteryLowHysteresis`, 5.0)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SELECT * FROM `deviceTypeSensor` WHERE `IDDeviceTypeSensor` = 1009;
--   SHOW COLUMNS FROM `deviceConfigSensor` LIKE 'BatteryDivider%';
--   SELECT `BatteryLowThreshold`, `BatteryLowHysteresis` FROM `serverConfig` WHERE `IDServerConfig` = 1;
--   SHOW COLUMNS FROM `deviceDiagnostic` LIKE 'LowBatteryNotifiedAt';
