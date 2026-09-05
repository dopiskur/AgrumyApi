-- EfRepository.Tenants (TenantsGetAllAsync/TenantGetByIdAsync/TenantEmergencyStopSetAsync) read/write
-- Tenant.EmergencyStopActive unconditionally, so every Tenant query fails with "Unknown column"
-- until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `tenant`
  ADD COLUMN IF NOT EXISTS `EmergencyStopActive` TINYINT(1) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW COLUMNS FROM `tenant` LIKE 'EmergencyStopActive';
