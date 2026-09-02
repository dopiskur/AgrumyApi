-- Roadmap #94 (firmware release process + configurable source) and #93 (firmware update UI).
--
-- The deviceFirmware table stops being a hand-inserted per-DeviceTypeID row and becomes a real
-- catalog keyed by board (PlatformIO environment) + semver version, populated from a configurable
-- source (GitHub Releases / this API's own local store / a custom manifest repository) - see
-- api.Models.FirmwareSource and api.Firmware.FirmwareCatalogService. The device reports which
-- board it runs (deviceDiagnostic.Board) so the right .bin is offered, and an admin can pin a
-- specific version per device (device.FirmwareTargetVersion) for a rollback.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every statement uses IF NOT EXISTS.

ALTER TABLE `deviceFirmware`
  ADD COLUMN IF NOT EXISTS `Board` VARCHAR(40) DEFAULT NULL,
  -- 0=GitHub, 1=Local, 2=Custom (api.Models.FirmwareSource) - which source created the row.
  ADD COLUMN IF NOT EXISTS `Source` INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `FileName` VARCHAR(120) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `SizeBytes` BIGINT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `Sha256` VARCHAR(64) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `PublishedAt` DATETIME(6) DEFAULT NULL;

-- MariaDB has no CREATE INDEX IF NOT EXISTS on older versions; the ALTER form does.
ALTER TABLE `deviceFirmware`
  ADD INDEX IF NOT EXISTS `ix_deviceFirmware_board_source` (`Board`, `Source`);

ALTER TABLE `deviceDiagnostic`
  ADD COLUMN IF NOT EXISTS `Board` VARCHAR(40) DEFAULT NULL;

ALTER TABLE `device`
  ADD COLUMN IF NOT EXISTS `FirmwareTargetVersion` VARCHAR(20) DEFAULT NULL;

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `FirmwareSource` INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `FirmwareGitHubRepository` VARCHAR(200) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `FirmwareCustomRepositoryUrl` VARCHAR(500) DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceFirmware`;
--   SHOW COLUMNS FROM `deviceDiagnostic` LIKE 'Board';
--   SHOW COLUMNS FROM `device` LIKE 'FirmwareTargetVersion';
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Firmware%';
