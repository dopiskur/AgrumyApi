-- Roadmap #383's new DeviceEventType.LoRaHardwareNotDetected=18, plus a pre-existing gap found
-- while adding it: 2026-09-06-event-type-catalog-336.sql's INSERT only covered IDs 1-13, but
-- api.Models.DeviceEventType already had I2CFault=14/RuleRejected=15/SensorStale=16/
-- LowMemoryReboot=17 at the time that migration was written - eventDevice.EventID's FK (added
-- by that same migration) has been silently rejecting any device pushing one of those four event
-- types on invent.hr ever since. Backfills 14-17 and adds 18 in one pass.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only creates
-- catalog rows in a brand-new (zero-table) database, never adds to an existing one.
--
-- SAFE TO RE-RUN: INSERT IGNORE, same as the original 336 migration.

INSERT IGNORE INTO `eventType` (`IDEventType`, `EventTypeName`) VALUES
  (14, 'I2CFault'),
  (15, 'RuleRejected'),
  (16, 'SensorStale'),
  (17, 'LowMemoryReboot'),
  (18, 'LoRaHardwareNotDetected');

-- Sanity check after running:
--   SELECT COUNT(*) FROM eventType; -- expect 18
