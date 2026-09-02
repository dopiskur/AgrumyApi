-- Roadmap #112: Device.TenantID int? -> int - root-cause fix for the null-vs-0 bug class raised
-- piecemeal across #96/#100/#106/#102/#108/#111. TenantID=0 is a real, meaningful default tenant
-- (user confirmed), not a "no tenant" sentinel, so the column should never have allowed NULL in
-- the first place - every one of those bugs was the same nullable-int fallback (`?? 0`) missing
-- from a new call site.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `device` table. Run
-- this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: the UPDATE only touches NULL rows (none left after the first run), and MODIFY
-- to the same NOT NULL type is a no-op.
--
-- CONFIRMED NO-OP on invent.hr as of the #96 investigation (0 existing `device` rows with
-- TenantID IS NULL) - the UPDATE below is included anyway so this migration is correct standalone
-- against any other database that might actually have one.
UPDATE `device` SET `TenantID` = 0 WHERE `TenantID` IS NULL;

ALTER TABLE `device` MODIFY COLUMN `TenantID` int(11) NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW COLUMNS FROM `device` LIKE 'TenantID';            -- expect Null = NO, Default = 0
--   SELECT COUNT(*) FROM `device` WHERE TenantID IS NULL;  -- expect 0
