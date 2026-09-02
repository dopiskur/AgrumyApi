-- Roadmap #34: device commands + queue - discrete, stateless, one-shot actions (Reboot/ForceOTA/
-- ForceConfigSync) issued on the EXISTING config-poll channel, real FIFO serialization (a proper
-- Command table, not a single "last write wins" counter) with per-(device, ActionType)
-- deduplication so repeated UI clicks cannot self-induce a reboot loop. See PROBLEM 1/2's
-- resolution notes on this roadmap item for the full history of why this shape was chosen over
-- the alternatives.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS `deviceCommand` (
  `IDDeviceCommand` INT NOT NULL AUTO_INCREMENT,
  `DeviceID` INT NOT NULL,
  -- ActionType: 1=Reboot, 2=ForceOTA, 3=ForceConfigSync (api.Models.CommandActionType).
  `ActionType` INT NOT NULL,
  -- Status: 0=Pending, 1=Acknowledged, 2=Executed, 3=Expired (api.Models.CommandStatus).
  `Status` INT NOT NULL DEFAULT 0,
  `IssuedAt` DATETIME(6) NOT NULL,
  `ExpiresAt` DATETIME(6) NOT NULL,
  `ExecutedAt` DATETIME(6) DEFAULT NULL,
  PRIMARY KEY (`IDDeviceCommand`),
  KEY `ix_deviceCommand_device_status` (`DeviceID`, `Status`),
  CONSTRAINT `fk_deviceCommand_device` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Roadmap #34: separate from ConfigVersion on purpose (issuing a command must not force a full
-- config re-apply, and vice versa) - bumped once per device every time a new Command row is
-- created for it (CommandQueueService.IssueCommandAsync), echoed to the firmware in the same
-- Config poll response as ConfigVersion.
ALTER TABLE `device` ADD COLUMN IF NOT EXISTS `CommandVersion` INT NOT NULL DEFAULT 0;

-- Sanity check after running:
--   SHOW CREATE TABLE `deviceCommand`;
--   SHOW COLUMNS FROM `device` LIKE 'CommandVersion';
