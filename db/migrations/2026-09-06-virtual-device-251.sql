-- Roadmap #251 modality B: server-internal registry of which device rows are fully virtual -
-- never exposed on any wire contract, consulted only by VirtualDeviceRunnerBackgroundService and
-- the /api/Simulation admin endpoints. See api.Dal.Entities.VirtualDeviceRow.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing one.
--
-- SAFE TO RE-RUN: CREATE TABLE IF NOT EXISTS - a second run is a no-op.

CREATE TABLE IF NOT EXISTS `virtualDevice` (
  `DeviceID` int(11) NOT NULL,
  `DateCreated` datetime(6) DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`DeviceID`),
  CONSTRAINT `FK_virtualDevice_device_DeviceID` FOREIGN KEY (`DeviceID`) REFERENCES `device` (`IDDevice`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `virtualDevice`;
