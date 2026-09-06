-- Roadmap #336: eventDevice.EventID/eventService.EventID were bare ints with no backing catalog
-- table, unlike every other "type" concept in the schema (deviceType/deviceTypeService/
-- deviceTypeRelay/deviceTypeSensor). Adds eventType (mirrors api.Models.DeviceEventType 1:1),
-- FKs it to both eventDevice.EventID and eventService.EventID, and FKs eventService.ServiceID
-- to the existing deviceTypeService catalog (HTTP/HTTPS/MQTT) - the naming already matched it,
-- eventService just never had the constraint.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only creates
-- tables in a brand-new (zero-table) database, never alters/adds to an existing one.
--
-- Confirmed against invent.hr before writing this: eventDevice's distinct EventID values are
-- {3,4,8,9,10,11,13}, all within DeviceEventType's defined range - the FK add below cannot fail
-- against real data. eventService is empty (0 rows, no code path writes it today).

CREATE TABLE IF NOT EXISTS `eventType` (
  `IDEventType` int(11) NOT NULL,
  `EventTypeName` varchar(64) DEFAULT NULL,
  PRIMARY KEY (`IDEventType`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO `eventType` (`IDEventType`, `EventTypeName`) VALUES
  (1, 'NoInternet'),
  (2, 'AuthFailed'),
  (3, 'ConfigSyncFailed'),
  (4, 'ConfigApplied'),
  (5, 'CrashLoopRollback'),
  (6, 'OtaFailed'),
  (7, 'BufferDiscarded'),
  (8, 'Offline'),
  (9, 'CommandExecuted'),
  (10, 'FirmwareUpdated'),
  (11, 'LowBattery'),
  (12, 'SafetyLimitTripped'),
  (13, 'Crash');

ALTER TABLE `eventDevice`
  ADD CONSTRAINT `FK_eventDevice_eventType_EventID` FOREIGN KEY (`EventID`) REFERENCES `eventType` (`IDEventType`);

ALTER TABLE `eventService`
  ADD CONSTRAINT `FK_eventService_eventType_EventID` FOREIGN KEY (`EventID`) REFERENCES `eventType` (`IDEventType`),
  ADD CONSTRAINT `FK_eventService_deviceTypeService_ServiceID` FOREIGN KEY (`ServiceID`) REFERENCES `deviceTypeService` (`IDDeviceTypeService`);

-- Sanity check after running:
--   SELECT COUNT(*) FROM eventType;                      -- expect 13
--   SHOW CREATE TABLE eventDevice;                        -- FK on EventID present
--   SHOW CREATE TABLE eventService;                        -- FK on EventID and ServiceID present
