-- Roadmap #384 (part 1/2 - rename): DeviceUnit -> DeviceFarmUnit, DeviceUnitZone -> DeviceFarmUnitZone.
-- Adds a new organizational level ABOVE Unit within the same tenant (Tenant -> Farm -> Unit ->
-- Zone -> Device) - see 2026-09-06-farm-hierarchy-new-table-384.sql for the new deviceFarm table
-- and deviceFarmUnit.DeviceFarmID column, added in a separate migration so this one stays a pure
-- rename (same "one concern per migration" discipline as 2026-09-06-devicetype-role-rename-341.sql).
--
-- Confirmed against invent.hr before writing this: every FK referencing deviceUnit/deviceUnitZone
-- (fk_device_unit, fk_deviceUnitZone_deviceUnit, fk_sensorData_deviceUnit,
-- fk_sensorData_deviceUnitZone) and every column literally named DeviceUnitID/DeviceUnitZoneID
-- across the whole schema (device, deviceUnitZone, deviceUnitZoneRule, ruleNotificationState,
-- sensorData) are covered below - deviceUnitZoneRule's own DeviceUnitID/DeviceUnitZoneID columns
-- have no FK constraint on invent.hr today (EF model has one, never manually migrated onto the
-- live DB - a pre-existing gap, out of scope here, unaffected either way by a plain column rename).
-- deviceUnitZoneRule itself is also renamed to deviceFarmUnitZoneRule (AgrumyDbContext.ToTable),
-- confirmed empty (0 rows) on invent.hr at the time this ran, so the rename carried zero data.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (old column/table/index names no longer exist)
-- rather than doing anything harmful - same rationale as 2026-09-06-devicetype-role-rename-341.sql.

ALTER TABLE `device` DROP FOREIGN KEY `fk_device_unit`;
ALTER TABLE `deviceUnitZone` DROP FOREIGN KEY `fk_deviceUnitZone_deviceUnit`;
ALTER TABLE `sensorData` DROP FOREIGN KEY `fk_sensorData_deviceUnit`;
ALTER TABLE `sensorData` DROP FOREIGN KEY `fk_sensorData_deviceUnitZone`;

ALTER TABLE `device`
  CHANGE COLUMN `DeviceUnitID` `DeviceFarmUnitID` int(11) DEFAULT NULL,
  CHANGE COLUMN `DeviceUnitZoneID` `DeviceFarmUnitZoneID` int(11) DEFAULT NULL;

ALTER TABLE `sensorData`
  CHANGE COLUMN `DeviceUnitID` `DeviceFarmUnitID` int(11) DEFAULT NULL,
  CHANGE COLUMN `DeviceUnitZoneID` `DeviceFarmUnitZoneID` int(11) DEFAULT NULL;

ALTER TABLE `deviceUnitZoneRule`
  CHANGE COLUMN `DeviceUnitID` `DeviceFarmUnitID` int(11) DEFAULT NULL,
  CHANGE COLUMN `DeviceUnitZoneID` `DeviceFarmUnitZoneID` int(11) DEFAULT NULL,
  CHANGE COLUMN `IDDeviceUnitZoneRule` `IDDeviceFarmUnitZoneRule` int(11) NOT NULL AUTO_INCREMENT;
ALTER TABLE `deviceUnitZoneRule`
  RENAME INDEX `ix_deviceUnitZoneRule_zone` TO `ix_deviceFarmUnitZoneRule_zone`,
  RENAME INDEX `ix_deviceUnitZoneRule_unit` TO `ix_deviceFarmUnitZoneRule_unit`,
  RENAME INDEX `ix_deviceUnitZoneRule_tenant` TO `ix_deviceFarmUnitZoneRule_tenant`;
RENAME TABLE `deviceUnitZoneRule` TO `deviceFarmUnitZoneRule`;

ALTER TABLE `ruleNotificationState`
  CHANGE COLUMN `DeviceUnitZoneID` `DeviceFarmUnitZoneID` int(11) NOT NULL;

ALTER TABLE `sensorData`
  RENAME INDEX `ix_sensorData_deviceUnitZone_date` TO `ix_sensorData_deviceFarmUnitZone_date`;

ALTER TABLE `deviceUnitZone`
  CHANGE COLUMN `DeviceUnitID` `DeviceFarmUnitID` int(11) NOT NULL;

ALTER TABLE `deviceUnitZone`
  CHANGE COLUMN `IDDeviceUnitZone` `IDDeviceFarmUnitZone` int(11) NOT NULL,
  CHANGE COLUMN `DeviceUnitZoneName` `DeviceFarmUnitZoneName` varchar(120) DEFAULT NULL;
ALTER TABLE `deviceUnitZone`
  RENAME INDEX `fk_deviceUnitZone_deviceUnit` TO `fk_deviceFarmUnitZone_deviceFarmUnit`;
RENAME TABLE `deviceUnitZone` TO `deviceFarmUnitZone`;

ALTER TABLE `deviceUnit`
  CHANGE COLUMN `IDDeviceUnit` `IDDeviceFarmUnit` int(11) NOT NULL,
  CHANGE COLUMN `DeviceUnitName` `DeviceFarmUnitName` varchar(100) DEFAULT NULL;
RENAME TABLE `deviceUnit` TO `deviceFarmUnit`;

ALTER TABLE `device`
  ADD CONSTRAINT `fk_device_farmUnit` FOREIGN KEY (`DeviceFarmUnitID`) REFERENCES `deviceFarmUnit` (`IDDeviceFarmUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE `deviceFarmUnitZone`
  ADD CONSTRAINT `fk_deviceFarmUnitZone_deviceFarmUnit` FOREIGN KEY (`DeviceFarmUnitID`) REFERENCES `deviceFarmUnit` (`IDDeviceFarmUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE `sensorData`
  ADD CONSTRAINT `fk_sensorData_deviceFarmUnit` FOREIGN KEY (`DeviceFarmUnitID`) REFERENCES `deviceFarmUnit` (`IDDeviceFarmUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  ADD CONSTRAINT `fk_sensorData_deviceFarmUnitZone` FOREIGN KEY (`DeviceFarmUnitZoneID`) REFERENCES `deviceFarmUnitZone` (`IDDeviceFarmUnitZone`) ON DELETE NO ACTION ON UPDATE NO ACTION;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceFarmUnit`; SHOW CREATE TABLE `deviceFarmUnitZone`; SHOW CREATE TABLE `deviceFarmUnitZoneRule`;
--   SELECT COUNT(*) FROM `deviceFarmUnit`;      -- expect 3 (matches pre-migration deviceUnit count)
--   SELECT COUNT(*) FROM `deviceFarmUnitZone`;  -- expect 6 (matches pre-migration deviceUnitZone count)
--   SELECT COUNT(*) FROM deviceUnit;            -- expect error, table no longer exists
