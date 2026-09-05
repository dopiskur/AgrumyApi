-- Roadmap #310: a handful of common WHERE-clause columns had no index. Investigation found two of the
-- roadmap's three named gaps already covered under a legacy name that predates the EF model owning the
-- schema - device.TenantID already has fk_device_tenant_idx, and deviceDiagnostic.DeviceID is that
-- table's own PRIMARY KEY (always indexed). Only user.TenantID and eventDevice(DeviceID, Date) were
-- genuinely missing; deviceCommand(DeviceID, Status) already exists (ix_deviceCommand_device_status,
-- added with the #34 command queue). This migration only adds the two real gaps - the corresponding EF
-- model (AgrumyDbContext) declares all three regardless, so a brand-new EnsureCreatedAsync install (no
-- legacy dump, no pre-existing indexes) gets device.TenantID too.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: both use IF NOT EXISTS (MariaDB 10.5+/11.4).

ALTER TABLE `user` ADD INDEX IF NOT EXISTS `ix_user_tenant` (`TenantID`);
ALTER TABLE `eventDevice` ADD INDEX IF NOT EXISTS `ix_eventDevice_device_date` (`DeviceID`, `Date`);

-- Sanity check after running:
--   SHOW INDEX FROM `user` WHERE Key_name = 'ix_user_tenant';
--   SHOW INDEX FROM `eventDevice` WHERE Key_name = 'ix_eventDevice_device_date';
