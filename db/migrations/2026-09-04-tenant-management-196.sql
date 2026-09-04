-- Roadmap #196: Tenant Management page toggle - gates the "Tenants" menu item in _Layout.cshtml
-- alongside the existing Global admin role check.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `serverConfig` table.
--
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS; the UPDATE only touches the one row still at 0.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `TenantManagementEnabled` TINYINT(1) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SELECT TenantManagementEnabled FROM `serverConfig` WHERE IDServerConfig = 1;  -- expect 0 (off by default)
