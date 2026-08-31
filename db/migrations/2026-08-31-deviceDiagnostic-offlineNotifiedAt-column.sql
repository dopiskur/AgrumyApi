-- Roadmap #40: offline-alert dedup bookkeeping - new `deviceDiagnostic.OfflineNotifiedAt` column.
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a column to a table that already
-- exists. Run this by hand against each database that predates this change (any install that
-- already had 2026-08-31-deviceDiagnostic-table.sql applied).
--
-- SAFE TO RE-RUN: guarded with IF NOT EXISTS.
--
-- NULL means "not currently in an alerted offline streak" - OfflineAlertBackgroundService sets it
-- when it notifies admins about a device going offline, and clears it back to NULL the tick it
-- observes the device reachable again, so the NEXT offline streak alerts fresh instead of staying
-- silent forever after the first incident.

ALTER TABLE `deviceDiagnostic`
  ADD COLUMN IF NOT EXISTS `OfflineNotifiedAt` DATETIME(6) DEFAULT NULL;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceDiagnostic`;
