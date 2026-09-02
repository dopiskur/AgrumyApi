-- Roadmap #82/#81: deviceUnit/deviceUnitZone were only ever placeholder scaffolding - two global
-- sentinel rows (IDDeviceUnit=0 'Default', IDDeviceUnitZone=0 'Disabled'), no admin-facing CRUD,
-- no tenant scoping, and a FK pointing the wrong way (deviceUnit.DeviceUnitZoneID: each Unit -> at
-- most one Zone). #81's dashboard needs the opposite - one Unit containing many Zones - and #82's
-- assignment screen needs both tables tenant-scoped (admin-created Units/Zones must not leak
-- across tenants, same standard as every other #47/#66/#102/#111 tenant-isolation fix).
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters existing deviceUnit/deviceUnitZone.
-- NOT SAFE TO RE-RUN: the DROP COLUMN/DROP FOREIGN KEY lines fail loudly on a second run (nothing
-- left to drop) rather than doing anything harmful.

-- 1) Tenant-scope both tables. Nullable, no FK - NULL means "global sentinel row", matching #112's
--    device.TenantID=0-is-real-tenant convention would be wrong here since these two rows
--    (IDDeviceUnit=0, IDDeviceUnitZone=0) are shared placeholders every tenant's unassigned
--    devices point at, not one tenant's real data.
ALTER TABLE `deviceUnit` ADD COLUMN `TenantID` int(11) DEFAULT NULL AFTER `IDDeviceUnit`;
ALTER TABLE `deviceUnitZone` ADD COLUMN `TenantID` int(11) DEFAULT NULL AFTER `IDDeviceUnitZone`;

-- 2) Real containment: every Zone belongs to exactly one Unit. Backfill existing rows (in practice
--    just the IDDeviceUnitZone=0 sentinel) to Unit 0, which the original dump already seeds.
ALTER TABLE `deviceUnitZone` ADD COLUMN `DeviceUnitID` int(11) DEFAULT NULL AFTER `TenantID`;
UPDATE `deviceUnitZone` SET `DeviceUnitID` = 0 WHERE `DeviceUnitID` IS NULL;
ALTER TABLE `deviceUnitZone` MODIFY COLUMN `DeviceUnitID` int(11) NOT NULL;
ALTER TABLE `deviceUnitZone`
  ADD CONSTRAINT `fk_deviceUnitZone_deviceUnit` FOREIGN KEY (`DeviceUnitID`) REFERENCES `deviceUnit` (`IDDeviceUnit`) ON DELETE NO ACTION ON UPDATE NO ACTION;

-- 3) Drop the old backwards Unit -> Zone pointer and its FK - superseded by #2 above. ZoneEnabled
--    is left alone (unused scaffolding, out of scope for this change).
ALTER TABLE `deviceUnit` DROP FOREIGN KEY `fk_deviceUnit_deviceUnitZone`;
ALTER TABLE `deviceUnit` DROP COLUMN `DeviceUnitZoneID`;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceUnit`;
--   SHOW COLUMNS FROM `deviceUnitZone`;
--   SHOW CREATE TABLE `deviceUnitZone`;  -- expect fk_deviceUnitZone_deviceUnit
