-- Roadmap #332: sensorData.DeviceUnitID/DeviceUnitZoneID used 0 as an "unassigned" sentinel,
-- unlike device.DeviceUnitID/DeviceUnitZoneID which #313 already made real-NULL. Unlike #313,
-- these columns ARE genuinely NOT NULL DEFAULT 0 here (confirmed against a real engine, not just
-- assumed) - MODIFY COLUMN to nullable must run BEFORE the backfill UPDATE, or the UPDATE itself
-- fails with "Column cannot be null".
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: MODIFY COLUMN restates the same DEFAULT NULL each time; the UPDATEs are no-ops
-- once every 0 is already NULL.

ALTER TABLE `sensorData` MODIFY COLUMN `DeviceUnitID` int(11) DEFAULT NULL;
ALTER TABLE `sensorData` MODIFY COLUMN `DeviceUnitZoneID` int(11) DEFAULT NULL;

UPDATE `sensorData` SET `DeviceUnitID` = NULL WHERE `DeviceUnitID` = 0;
UPDATE `sensorData` SET `DeviceUnitZoneID` = NULL WHERE `DeviceUnitZoneID` = 0;

-- Sanity check after running:
--   SELECT COUNT(*) FROM sensorData WHERE DeviceUnitID = 0 OR DeviceUnitZoneID = 0;  -- expect 0
--   SHOW COLUMNS FROM sensorData LIKE 'DeviceUnit%';                                 -- expect DEFAULT NULL on both
