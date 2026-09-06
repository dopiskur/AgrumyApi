-- Roadmap #385 (merges former #333+#382): adds a real numeric PK to deviceType, replacing
-- Kit-as-PK carried over unchanged from deviceTypeKit's original shape by #341's rename -
-- every sibling DeviceType* table already follows the ID<Name> pattern, this one didn't.
-- Kit stays a required, unique string - FK_deviceDiagnostic_deviceType_Kit and
-- FK_device_deviceType_ManualKit keep targeting Kit unchanged (business logic - auto-
-- registration, the Web dropdown - is keyed by the Kit string, not this new numeric PK), just
-- against a unique index instead of the PK.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (IDDeviceType already exists) rather than
-- doing anything harmful - same rationale as 2026-09-05-gateway-rename-308.sql.

ALTER TABLE `deviceDiagnostic` DROP FOREIGN KEY `FK_deviceDiagnostic_deviceType_Kit`;
ALTER TABLE `device` DROP FOREIGN KEY `FK_device_deviceType_ManualKit`;

ALTER TABLE `deviceType`
  DROP PRIMARY KEY,
  ADD COLUMN `IDDeviceType` int(11) NOT NULL AUTO_INCREMENT FIRST,
  ADD PRIMARY KEY (`IDDeviceType`),
  ADD UNIQUE KEY `ux_deviceType_kit` (`Kit`);

ALTER TABLE `deviceDiagnostic`
  ADD CONSTRAINT `FK_deviceDiagnostic_deviceType_Kit` FOREIGN KEY (`Kit`) REFERENCES `deviceType` (`Kit`);
ALTER TABLE `device`
  ADD CONSTRAINT `FK_device_deviceType_ManualKit` FOREIGN KEY (`ManualKit`) REFERENCES `deviceType` (`Kit`);

-- Curated catalog additions (roadmap #385's board list) - ControllerCapable=0 for these four,
-- unlike KC868-A6/ESP32-S3-Relay-6CH above them, none is a relay board.
INSERT INTO `deviceType` (`Kit`, `ControllerCapable`, `PinoutJson`)
SELECT 'Heltec V3', 0, NULL WHERE NOT EXISTS (SELECT 1 FROM `deviceType` WHERE `Kit` = 'Heltec V3');
INSERT INTO `deviceType` (`Kit`, `ControllerCapable`, `PinoutJson`)
SELECT 'Heltec V4', 0, NULL WHERE NOT EXISTS (SELECT 1 FROM `deviceType` WHERE `Kit` = 'Heltec V4');
INSERT INTO `deviceType` (`Kit`, `ControllerCapable`, `PinoutJson`)
SELECT 'ESP32Dev', 0, NULL WHERE NOT EXISTS (SELECT 1 FROM `deviceType` WHERE `Kit` = 'ESP32Dev');
INSERT INTO `deviceType` (`Kit`, `ControllerCapable`, `PinoutJson`)
SELECT 'LILYGO T-ETH-ELite ESP32-S3 (SX1302)', 0, NULL WHERE NOT EXISTS (SELECT 1 FROM `deviceType` WHERE `Kit` = 'LILYGO T-ETH-ELite ESP32-S3 (SX1302)');

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceType`;   -- IDDeviceType PK, Kit UNIQUE KEY
--   SELECT * FROM `deviceType`;       -- expect 7 rows (3 existing + 4 new)
--   SHOW CREATE TABLE `device`;       -- FK_device_deviceType_ManualKit present, unchanged target
--   SHOW CREATE TABLE `deviceDiagnostic`; -- FK_deviceDiagnostic_deviceType_Kit present, unchanged target
