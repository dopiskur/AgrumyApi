-- Roadmap #313: device.DeviceUnitID/DeviceUnitZoneID stop using 0 as an "unassigned" sentinel and
-- use real NULL instead. Investigation found both columns were ALREADY nullable on this database
-- (`int(11) DEFAULT 0`, no NOT NULL) - the FK to deviceUnit/deviceUnitZone never required NOT NULL,
-- so no MODIFY COLUMN is needed here. The actual sentinel source was Agrumy.Shared's Device DTO
-- defaulting both properties to 0 in C# (fixed alongside this migration, Agrumy.Shared/Models/
-- Device.cs) - this migration only cleans up the DATA and the column DEFAULT that resulted from it.
--
-- The IDDeviceUnit=0 "Default" / IDDeviceUnitZone=0 "Disabled" sentinel ROWS themselves are left
-- alone - that is a separate question (see #313's own step 6), not decided here.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: the UPDATEs are no-ops once every 0 is already NULL; MODIFY COLUMN restates the
-- same DEFAULT NULL each time.

UPDATE `device` SET `DeviceUnitID` = NULL WHERE `DeviceUnitID` = 0;
UPDATE `device` SET `DeviceUnitZoneID` = NULL WHERE `DeviceUnitZoneID` = 0;

ALTER TABLE `device` MODIFY COLUMN `DeviceUnitID` int(11) DEFAULT NULL;
ALTER TABLE `device` MODIFY COLUMN `DeviceUnitZoneID` int(11) DEFAULT NULL;

-- Sanity check after running:
--   SELECT COUNT(*) FROM device WHERE DeviceUnitID = 0 OR DeviceUnitZoneID = 0;  -- expect 0
--   SHOW COLUMNS FROM device LIKE 'DeviceUnit%';                                 -- expect DEFAULT NULL on both
