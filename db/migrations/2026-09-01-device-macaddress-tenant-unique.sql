-- Roadmap #102: composite UNIQUE(MacAddress, TenantID) on `device`.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `device` table. Run
-- this by hand against each database that predates this change.
--
-- WHY COMPOSITE, NOT A BARE MacAddress UNIQUE: a physical device is legitimately resold across
-- tenants (old tenant keeps its historical row, new tenant registers a "new" row with the same
-- MAC) - DeviceGetAsync's device-registration lookup has always been TenantID-scoped for this
-- reason. What was missing is a DB-level guard against a duplicate register request WITHIN the
-- same tenant (double click, firmware retry mid network-hiccup): DeviceAddAsync's read (own
-- DbContext) and the caller's prior "does it exist" check (a separate DbContext) are not
-- serialized against each other, so two parallel requests with the same MAC and tenant can both
-- pass the check before either commits.
--
-- SAFE TO RE-RUN: guarded via information_schema, same pattern as
-- 2026-08-30-user-activation-columns.sql's ActivationTokenHash_UNIQUE index.
--
-- WILL FAIL LOUDLY (duplicate-key error) IF EXISTING DUPLICATE (MacAddress, TenantID) ROWS ARE
-- PRESENT - run the sanity-check SELECT below FIRST and resolve any hits before applying.

-- Pre-check (run manually, review output before proceeding):
--   SELECT MacAddress, TenantID, COUNT(*) c FROM `device`
--   WHERE MacAddress IS NOT NULL GROUP BY MacAddress, TenantID HAVING c > 1;

SET @index_exists = (
  SELECT COUNT(*) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'device' AND index_name = 'MacAddress_TenantID_UNIQUE'
);
SET @add_index_sql = IF(@index_exists = 0,
  'ALTER TABLE `device` ADD UNIQUE INDEX `MacAddress_TenantID_UNIQUE` (`MacAddress`, `TenantID`)',
  'SELECT 1');
PREPARE stmt FROM @add_index_sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Sanity check after running:
--   SHOW INDEX FROM `device` WHERE Key_name = 'MacAddress_TenantID_UNIQUE';
