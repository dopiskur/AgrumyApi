-- Roadmap #384 (part 2/2 - new table): adds the actual Farm level, above Unit, within the same
-- tenant (Tenant -> Farm -> Unit -> Zone -> Device). See 2026-09-06-farm-hierarchy-rename-384.sql
-- for the DeviceUnit -> DeviceFarmUnit rename this builds on.
--
-- deviceFarmUnit.DeviceFarmID is nullable and optional by design - alfa phase, no live data to
-- backfill, a Farm-less Unit stays fully valid ("unassigned"), same rule as an unzoned device.
-- deviceFarmUnitZoneRule.DeviceFarmID is the 4th rule-scope column (Farm), alongside the existing
-- DeviceFarmUnitID/DeviceFarmUnitZoneID - exactly one of the three (or none, for Global) is set per
-- row, enforced in DeviceFarmUnitApiController, not the DB (same rule as the existing two).
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (table/column already exists) rather than doing
-- anything harmful.

CREATE TABLE `deviceFarm` (
  `IDDeviceFarm` int(11) NOT NULL AUTO_INCREMENT,
  `TenantID` int(11) DEFAULT NULL,
  `DeviceFarmName` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`IDDeviceFarm`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `deviceFarmUnit`
  ADD COLUMN `DeviceFarmID` int(11) DEFAULT NULL AFTER `DeviceFarmUnitName`,
  ADD KEY `ix_deviceFarmUnit_farm` (`DeviceFarmID`),
  ADD CONSTRAINT `fk_deviceFarmUnit_deviceFarm` FOREIGN KEY (`DeviceFarmID`) REFERENCES `deviceFarm` (`IDDeviceFarm`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `deviceFarmUnitZoneRule`
  ADD COLUMN `DeviceFarmID` int(11) DEFAULT NULL AFTER `TenantID`,
  ADD KEY `ix_deviceFarmUnitZoneRule_farm` (`DeviceFarmID`);
-- No FK on deviceFarmUnitZoneRule.DeviceFarmID -> deviceFarm - same "no DB-level FK, scope enforced
-- in the API" precedent as its existing DeviceFarmUnitID/DeviceFarmUnitZoneID columns.

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceFarm`;
--   SHOW COLUMNS FROM `deviceFarmUnit` LIKE 'DeviceFarmID';
--   SHOW COLUMNS FROM `deviceFarmUnitZoneRule` LIKE 'DeviceFarmID';
--   SELECT COUNT(*) FROM deviceFarm; -- expect 0
