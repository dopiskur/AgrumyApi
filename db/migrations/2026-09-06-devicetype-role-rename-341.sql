-- Roadmap #341: "DeviceType" collided between the device ROLE (Sensor/Controller/Both/None,
-- IDDeviceType 0/1/2/3, DeviceController.Edit's literal switch) and the physical kit catalog
-- (deviceTypeKit, keyed by Kit string). Renames only the ROLE side to "DeviceRole", freeing
-- "DeviceType" for the kit catalog's own future rename/expansion (separate, not done here).
-- deviceFirmware.DeviceTypeID is left untouched - it is a legacy, unenforced-FK column (no FK
-- constraint exists on it today; see FirmwareCatalogService's "legacy per-DeviceTypeID row"
-- comment) and is not part of this rename's scope.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing table.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (old column/table/index names no longer exist)
-- rather than doing anything harmful - same rationale as 2026-09-05-gateway-rename-308.sql.

ALTER TABLE `device`
  DROP FOREIGN KEY `fk_device_deviceType`;

ALTER TABLE `device`
  CHANGE COLUMN `DeviceTypeID` `DeviceRoleID` int(11) DEFAULT 0;

ALTER TABLE `device`
  RENAME INDEX `fk_device_deviceType_idx` TO `fk_device_deviceRole_idx`;

ALTER TABLE `deviceType`
  CHANGE COLUMN `IDDeviceType` `IDDeviceRole` int(11) NOT NULL AUTO_INCREMENT,
  CHANGE COLUMN `DeviceTypeName` `DeviceRoleName` varchar(100) DEFAULT NULL;

RENAME TABLE `deviceType` TO `deviceRole`;

ALTER TABLE `device`
  ADD CONSTRAINT `fk_device_deviceRole` FOREIGN KEY (`DeviceRoleID`) REFERENCES `deviceRole` (`IDDeviceRole`) ON DELETE NO ACTION ON UPDATE NO ACTION;

-- Sanity check after running:
--   SHOW COLUMNS FROM `device` LIKE 'DeviceRoleID'; SHOW COLUMNS FROM `device` LIKE 'DeviceTypeID'; -- expect 1 row, then 0 rows
--   SHOW CREATE TABLE `deviceRole`;
--   SELECT COUNT(*) FROM `deviceType`; -- expect error, table no longer exists
