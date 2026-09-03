-- Roadmap #149: kit-based controller-capability detection - Board (existing) identifies which
-- firmware BINARY a device runs (roadmap #94, used for OTA matching); Kit is a separate signal
-- naming the PHYSICAL commercial board (KC868-A6, ESP32-S3-Relay-6CH) so the server can tell "this
-- device has real relay hardware wired" apart from the admin having to guess via DeviceType. An
-- unrecognized/empty Kit (every device before this migration, and any DIY/generic build) falls
-- back to today's behavior unchanged - DeviceType/DeviceControllerEnabled stays the sole,
-- admin-controlled signal for those (EfRepository.DeviceFleetGetAsync's ControllerCapable OR).
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS + INSERT IGNORE.

CREATE TABLE IF NOT EXISTS `deviceTypeKit` (
  `Kit` VARCHAR(64) NOT NULL,
  `ControllerCapable` TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Kit`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO `deviceTypeKit` (`Kit`, `ControllerCapable`) VALUES
  ('KC868-A6', 1),
  ('ESP32-S3-Relay-6CH', 1);

ALTER TABLE `deviceDiagnostic` ADD COLUMN IF NOT EXISTS `Kit` VARCHAR(64) DEFAULT NULL;

-- Sanity check after running:
--   SELECT * FROM `deviceTypeKit`;
--   SHOW COLUMNS FROM `deviceDiagnostic` LIKE 'Kit';
