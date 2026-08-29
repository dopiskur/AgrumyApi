-- Roadmap #3 - OTA firmware update: migrate the existing `deviceFirmware` table.
--
-- WHY THIS IS MANUAL:
-- Agrumy.Api's EnsureSchemaAsync() short-circuits as soon as the `device` table
-- exists (SqlRepository.cs: `if (await TableExistsAsync(connection, SchemaScripts.KeyTable)) return;`).
-- On any database that already has data it therefore does NOT re-apply SchemaScripts.cs,
-- so changing the T_DeviceFirmware definition there only affects brand-new databases.
-- Run this file by hand against each existing database (e.g. agrumyapi on invent.hr).
--
-- Idempotency: MariaDB has no "ADD COLUMN IF NOT EXISTS" in all versions, so this is
-- written to be run once. Re-running will error on the ADD COLUMN if DateAdded already
-- exists - that error is safe to ignore.
--
-- Original definition:
--   `IDDeviceFirmware` int(11) NOT NULL,
--   `Version` decimal(10,0) DEFAULT NULL,     -- cannot store "0.1.1"
--   (no DateAdded, no AUTO_INCREMENT)

ALTER TABLE `deviceFirmware`
  MODIFY COLUMN `Version` VARCHAR(20) DEFAULT NULL;

ALTER TABLE `deviceFirmware`
  ADD COLUMN `DateAdded` DATETIME DEFAULT CURRENT_TIMESTAMP;

-- Existing rows get CURRENT_TIMESTAMP for DateAdded at ALTER time, so any pre-existing
-- rows for the same DeviceTypeID will tie. If that matters, set DateAdded explicitly on
-- the historical rows before relying on "latest wins", e.g.:
--   UPDATE `deviceFirmware` SET `DateAdded` = '2025-01-01 00:00:00' WHERE `IDDeviceFirmware` = <id>;

ALTER TABLE `deviceFirmware`
  MODIFY COLUMN `IDDeviceFirmware` INT(11) NOT NULL AUTO_INCREMENT;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceFirmware`;
--   SELECT * FROM `deviceFirmware` ORDER BY `DateAdded` DESC;
