-- Roadmap #11: weather-based irrigation adjustment. Adds the install-wide forecast config/state to
-- serverConfig and the per-zone opt-in veto to deviceUnitZone. NOT a 5th ConditionType/rule (see
-- deviceUnitZoneRule) - OR-combining rules means a Weather rule could only ever ADD a reason to
-- turn WaterPump on, never suppress one already decided by another rule (user decision,
-- 2026-09-04). Instead this is a final AND-NOT gate the device applies after the OR of all
-- WaterPump rules, same architectural slot as the existing #36 safety limits.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every statement uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  -- Location the OpenWeatherMap forecast is pulled for - one point for the whole install (same v1
  -- simplification as ScheduleTimeZone). NULL = not configured yet, WeatherEvaluator stays inert.
  ADD COLUMN IF NOT EXISTS `WeatherLocationLat` DOUBLE DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WeatherLocationLon` DOUBLE DEFAULT NULL,
  -- Admin-editable poll cadence (default 15 - user decision 2026-09-04, OpenWeatherMap's free tier
  -- allows 1000 calls/day). Live-editable without an app restart - see WeatherEvaluator's remarks.
  ADD COLUMN IF NOT EXISTS `WeatherPollIntervalMinutes` INT DEFAULT NULL,
  -- Rain-probability percentage (0-100) at or above which WaterPump is skipped (default 50 - user
  -- decision 2026-09-04).
  ADD COLUMN IF NOT EXISTS `WeatherRainSkipThreshold` DOUBLE DEFAULT NULL,
  -- Computed by WeatherEvaluator only (api.Dal.EfRepository.ServerConfigWeatherStateSetAsync) -
  -- never written by the admin Server Settings form. NOT NULL/default false: always has a concrete
  -- value, same reasoning as AllowSelfServiceTenantCreation.
  ADD COLUMN IF NOT EXISTS `WeatherRainPredicted` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `WeatherCheckedAtUtc` DATETIME DEFAULT NULL;

ALTER TABLE `deviceUnitZone`
  -- Per-zone opt-in (default off) - not every zone waters something rain makes redundant. Combined
  -- server-side with serverConfig.WeatherRainPredicted into the single wire flag
  -- deviceConfigController.skipWaterPumpForRain (DeviceApiController.BuildDeviceConfigAsync).
  ADD COLUMN IF NOT EXISTS `SkipWaterPumpWhenRainPredicted` TINYINT(1) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Weather%';
--   SHOW COLUMNS FROM `deviceUnitZone` LIKE 'SkipWaterPump%';
