-- Roadmap #91: fresh install had no seed data at all - empty deviceType*/deviceType lookup
-- tables and no user account to log in with, blocking roadmap #30 completely. The lookup-table
-- and bootstrap-admin rows themselves are seeded automatically by EfRepository.EnsureSchemaAsync
-- (SeedDeviceTypeLookupsAsync/SeedBootstrapAdminAsync) on next startup against ANY database whose
-- `user` table is completely empty - no manual SQL needed for that part.
--
-- This file exists only for the narrower edge case #91 is actually about: a self-hosted install
-- that already ran an OLDER build of this code against an empty database before upgrading to this
-- fix. That older build's EnsureCreatedAsync already created `user` with PwdHash/PwdSalt NOT NULL
-- (the pre-#91 model) - the table exists but has zero rows, so the automatic seeding above WILL
-- try to run, and its NULL-password bootstrap row will violate that leftover NOT NULL constraint.
-- Run this by hand against exactly that situation before starting the upgraded binary.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only provisions a
-- brand-new (zero-table) database, never alters an existing `user` table.
--
-- SAFE TO RE-RUN: MODIFY to the same nullability is a no-op.

ALTER TABLE `user` MODIFY `PwdHash` TEXT NULL;
ALTER TABLE `user` MODIFY `PwdSalt` VARCHAR(128) NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'Pwd%';   -- both YES under Null
