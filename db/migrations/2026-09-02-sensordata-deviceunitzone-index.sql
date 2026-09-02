-- Roadmap #116 rule (3): the new 24h-trend sparkline query filters sensorData directly by
-- DeviceUnitZoneID (+ DateCreated range) - the existing ix_sensorData_device_tenant_date index
-- (DeviceID, TenantID, DateCreated) does not help that shape at all, since it isn't keyed by
-- zone. Unit-level trend queries reuse this same index by first resolving the unit's zone ids and
-- filtering with DeviceUnitZoneID IN (...), so one new index covers both cube levels - no second
-- index on DeviceUnitID needed.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: guarded with a duplicate-key-safe pattern via information_schema check below is
-- unnecessary here - MySQL's CREATE INDEX has no native IF NOT EXISTS before 8.0.29/MariaDB
-- equivalent gaps, so this uses the same ADD INDEX IF NOT EXISTS MariaDB/MySQL 8+ extension the
-- rest of this project's migrations rely on already having a compatible server version for.

ALTER TABLE `sensorData`
  ADD INDEX IF NOT EXISTS `ix_sensorData_deviceUnitZone_date` (`DeviceUnitZoneID`, `DateCreated`);

-- Sanity check after running:
--   SHOW INDEX FROM `sensorData` WHERE Key_name = 'ix_sensorData_deviceUnitZone_date';
