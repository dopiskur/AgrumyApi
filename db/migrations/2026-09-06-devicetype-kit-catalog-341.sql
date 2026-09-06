-- Roadmap #341, kit-catalog half (the remaining part after the DeviceType->DeviceRole rename
-- already applied by 2026-09-06-devicetype-role-rename-341.sql). The OLD deviceTypeKit table
-- (Kit string + ControllerCapable bool, two curated rows) becomes the NEW, expanded deviceType -
-- the name freed up by the role rename. Adds PinoutJson (unopinionated per-kit GPIO layout blob,
-- shape TBD per kit - null until someone documents one), a real FK from deviceDiagnostic.Kit to
-- it (api.Dal.EfRepository.EnsureDeviceTypeRegisteredAsync auto-registers an unrecognized kit
-- BEFORE every diagnostic write, so this FK can never block a device's own heartbeat), and
-- device.ManualKit (admin fallback for a device whose firmware never auto-reports a Kit, e.g. a
-- generic esp32dev/esp32s3usbotg build) with its own FK to the same catalog.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (old table/column names no longer exist) rather
-- than doing anything harmful - same rationale as 2026-09-05-gateway-rename-308.sql.

RENAME TABLE `deviceTypeKit` TO `deviceType`;

ALTER TABLE `deviceType`
  ADD COLUMN `PinoutJson` longtext DEFAULT NULL;

-- Every currently-reported Kit must already be a "" (deviceDiagnostic.Kit's empty-string
-- generic-build convention) or a value already curated in deviceType, or this ALTER's FK add
-- below would fail against real data - confirmed against invent.hr before writing this
-- migration (single existing row, Kit=''). "" is normalized to NULL here (not kept as a special
-- catalog row) since api.Models.DeviceFleetStatus's ControllerCapable check already treats
-- "" and NULL identically, and NULL is what lets the FK skip validation for a device with no
-- specific kit, rather than needing an empty-string row to exist in the catalog.
UPDATE `deviceDiagnostic` SET `Kit` = NULL WHERE `Kit` = '';

ALTER TABLE `deviceDiagnostic`
  ADD CONSTRAINT `FK_deviceDiagnostic_deviceType_Kit` FOREIGN KEY (`Kit`) REFERENCES `deviceType` (`Kit`);

ALTER TABLE `device`
  ADD COLUMN `ManualKit` varchar(64) DEFAULT NULL,
  ADD CONSTRAINT `FK_device_deviceType_ManualKit` FOREIGN KEY (`ManualKit`) REFERENCES `deviceType` (`Kit`);

-- Roadmap #251 modality B's software-only kit-type - seeded here (not left to app-seed-on-empty
-- logic, since the deviceType table on an existing install is never empty by the time this runs).
INSERT INTO `deviceType` (`Kit`, `ControllerCapable`, `PinoutJson`)
SELECT 'VirtualDevice', 1, NULL WHERE NOT EXISTS (SELECT 1 FROM `deviceType` WHERE `Kit` = 'VirtualDevice');

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceType`;             -- PinoutJson present, VirtualDevice row present
--   SHOW CREATE TABLE `deviceDiagnostic`;        -- FK on Kit present
--   SHOW CREATE TABLE `device`;                  -- ManualKit column + FK present
--   SELECT COUNT(*) FROM deviceDiagnostic WHERE Kit = '';  -- expect 0
