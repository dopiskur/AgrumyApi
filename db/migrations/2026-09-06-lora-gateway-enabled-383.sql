-- Roadmap #383 - per-device "LoRa Gateway enabled" toggle (standalone/dual-role device relaying
-- LoRa private-protocol uplinks via its own WiFi/HTTP connection instead of a separate serial-
-- attached bridge board + Agrumy.Gateway process). Same pattern as the existing SleepDeepEnabled
-- column - nullable, DEFAULT 0, no FK.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (column already exists) rather than doing
-- anything harmful.

ALTER TABLE `device`
  ADD COLUMN `LoRaGatewayEnabled` tinyint(1) DEFAULT 0 AFTER `SleepDeepEnabled`;

-- Sanity check after running:
--   SHOW CREATE TABLE `device`; -- LoRaGatewayEnabled tinyint(1) DEFAULT 0, right after SleepDeepEnabled
