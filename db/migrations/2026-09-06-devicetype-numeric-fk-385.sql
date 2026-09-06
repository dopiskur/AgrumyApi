-- Roadmap #385 (part 2, follow-up to 2026-09-06-devicetype-id-pk-385.sql) - replaces the
-- remaining Kit-string FKs with real numeric FKs to deviceType.IDDeviceType, per the user's
-- explicit "the database has relations for a reason, use them" decision. The wire protocol
-- (firmware -> server Kit string) is UNCHANGED - only server-side storage moves off the string.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (DeviceTypeID/ManualDeviceTypeID already
-- exist) rather than doing anything harmful - same rationale as 2026-09-06-devicetype-id-pk-385.sql.

ALTER TABLE `deviceDiagnostic`
  DROP FOREIGN KEY `FK_deviceDiagnostic_deviceType_Kit`,
  ADD COLUMN `DeviceTypeID` int(11) NULL AFTER `Kit`;

ALTER TABLE `device`
  DROP FOREIGN KEY `FK_device_deviceType_ManualKit`,
  ADD COLUMN `ManualDeviceTypeID` int(11) NULL AFTER `ManualKit`;

-- Backfill from the string columns before they're dropped.
UPDATE `deviceDiagnostic` d
  JOIN `deviceType` t ON t.`Kit` = d.`Kit`
  SET d.`DeviceTypeID` = t.`IDDeviceType`;

UPDATE `device` dv
  JOIN `deviceType` t ON t.`Kit` = dv.`ManualKit`
  SET dv.`ManualDeviceTypeID` = t.`IDDeviceType`;

ALTER TABLE `deviceDiagnostic` DROP COLUMN `Kit`;
ALTER TABLE `device` DROP COLUMN `ManualKit`;

ALTER TABLE `deviceDiagnostic`
  ADD CONSTRAINT `FK_deviceDiagnostic_deviceType_DeviceTypeID` FOREIGN KEY (`DeviceTypeID`) REFERENCES `deviceType` (`IDDeviceType`);
ALTER TABLE `device`
  ADD CONSTRAINT `FK_device_deviceType_ManualDeviceTypeID` FOREIGN KEY (`ManualDeviceTypeID`) REFERENCES `deviceType` (`IDDeviceType`);

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceDiagnostic`; -- Kit column gone, DeviceTypeID present with FK to deviceType.IDDeviceType
--   SHOW CREATE TABLE `device`;           -- ManualKit column gone, ManualDeviceTypeID present with FK to deviceType.IDDeviceType
--   SELECT COUNT(*) FROM deviceDiagnostic WHERE DeviceTypeID IS NULL; -- compare against pre-migration COUNT(*) WHERE Kit IS NULL/'' - should match
--   SELECT COUNT(*) FROM device WHERE ManualDeviceTypeID IS NULL;     -- compare against pre-migration COUNT(*) WHERE ManualKit IS NULL - should match
