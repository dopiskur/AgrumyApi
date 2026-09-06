-- Every other "device"-family table follows device<Modifier> (prefix-first): deviceUnit,
-- deviceType, deviceCommand, deviceDiagnostic, deviceSimulation, deviceFirmware, etc.
-- virtualDevice was the one outlier with "device" as a suffix instead of a prefix.
--
-- No column rename here (unlike 2026-09-06-devicetype-role-rename-341.sql), so the existing
-- FK on DeviceID needs no drop/recreate - MySQL updates a renamed table's own FK metadata
-- automatically, only the OTHER side's constraint referencing this table by name would need
-- touching, and nothing references virtualDevice by FK.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql.
-- NOT SAFE TO RE-RUN: a second run fails loudly (virtualDevice no longer exists) rather than
-- doing anything harmful - same rationale as 2026-09-05-gateway-rename-308.sql.

RENAME TABLE `virtualDevice` TO `deviceVirtual`;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceVirtual`;
--   SELECT COUNT(*) FROM `virtualDevice`; -- expect error, table no longer exists
