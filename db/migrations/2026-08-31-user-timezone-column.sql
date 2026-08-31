-- Roadmap #71 follow-up: per-user display time zone. Stored as an IANA id ("Europe/Zagreb"),
-- never a UTC offset - offsets shift with DST, TimeZoneInfo resolves IANA ids correctly.
-- NULL = user never picked one; the UI treats that as "show UTC" (api.Utils.TimeZoneHelper).
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing `user` table.
-- Run this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: ADD COLUMN IF NOT EXISTS; no data backfill needed (NULL default is the
-- intended state for every existing user).

ALTER TABLE `user`
  ADD COLUMN IF NOT EXISTS `TimeZone` VARCHAR(64) NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `user` LIKE 'TimeZone';   -- expect varchar(64), NULL allowed
