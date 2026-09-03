-- Roadmap #41: blank-chip web installer (esp-web-tools). Four nullable sibling columns on the
-- SAME deviceFirmware row as its OTA file - one release publishes at most one merged, blank-chip-
-- flashable image PER board+version (bootloader + partition table + boot_app0 + the OTA app, all
-- at their real flash offsets, merged to one file at offset 0), alongside the existing OTA .bin -
-- see api.Firmware.FirmwareVersion.TryParseFullImageFileName and AgrumyFirmware's release.yml
-- merge_bin step. Null on every row until the next release published after this migration - OTA is
-- entirely unaffected either way (nothing here changes FileName/Url/Sha256/SizeBytes).
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every statement uses IF NOT EXISTS.

-- FullImageUrl is MEDIUMTEXT to match the existing Url column's actual live type (verified via
-- SHOW COLUMNS on invent.hr before writing this) - not a VARCHAR cap that could truncate a long URL.
ALTER TABLE `deviceFirmware`
  ADD COLUMN IF NOT EXISTS `FullImageFileName` VARCHAR(120) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `FullImageUrl` MEDIUMTEXT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `FullImageSizeBytes` BIGINT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `FullImageSha256` VARCHAR(64) DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceFirmware` LIKE 'FullImage%';
