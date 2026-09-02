-- Roadmap #36: WaterPump-only device-side hard safety limits (max continuous run time + cooldown
-- before the next attempt) - a ServerConfig-wide default pair, mirroring the #10 hysteresis
-- pattern, plus a per-device DeviceConfigController override (unlike #12's battery pair, which has
-- no per-device override since it is a server-side alert, not on-device relay logic).
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every ALTER TABLE uses IF NOT EXISTS; the UPDATE only touches rows that are
-- still NULL, so re-running never clobbers a value an admin already edited.

ALTER TABLE `deviceConfigController`
  ADD COLUMN IF NOT EXISTS `WaterPumpMaxRunSeconds` INT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WaterPumpCooldownSeconds` INT DEFAULT NULL;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `WaterPumpMaxRunSeconds` INT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WaterPumpCooldownSeconds` INT DEFAULT NULL;

-- Row 1 predates this migration on any install that already had a serverConfig row - backfill the
-- new columns with AgrumySettings' WaterPumpMaxRunSeconds/CooldownSeconds defaults (30 min / 5 min).
-- Existing DEVICES are deliberately NOT backfilled here (deviceConfigController stays NULL = "no
-- limit enforced yet") - unlike a brand new device (seeded from serverConfig at creation time), an
-- already-deployed pump's safe operating numbers are the admin's call to make explicitly, not a
-- silent retroactive change from this migration.
UPDATE `serverConfig`
SET `WaterPumpMaxRunSeconds`  = COALESCE(`WaterPumpMaxRunSeconds`,  1800),
    `WaterPumpCooldownSeconds` = COALESCE(`WaterPumpCooldownSeconds`, 300)
WHERE `IDServerConfig` = 1;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceConfigController` LIKE 'WaterPump%Seconds';
--   SELECT `WaterPumpMaxRunSeconds`, `WaterPumpCooldownSeconds` FROM `serverConfig` WHERE `IDServerConfig` = 1;
