-- Roadmap #21: rule engine + zone-based config. Threshold/interval/schedule/#36 safety limits move
-- from the per-device deviceConfigController row to the ZONE (deviceUnitZone) - a device reads the
-- rules of whichever zone it is assigned to at every config poll (DeviceApiController.
-- BuildDeviceConfigAsync), instead of its own fixed per-device fields. Relay-pin mapping
-- (relay1..relay8) stays on deviceConfigController, unchanged - a physical/hardware fact, not a
-- rule. Closes #137 as a side effect: a replacement controller assigned to the same zone
-- immediately runs that zone's existing rules, no copy-config step needed.
--
-- The old deviceConfigController threshold/interval/hysteresis/#36 columns and the deviceScheduleSlot
-- table are intentionally NOT dropped or migrated - full reset to defaults (alfa-phase user
-- decision, 2026-09-04: no real users affected, admin manually re-enters rules per zone after this
-- deploy). Those old columns become dead/unused, kept only so a DROP is not required (matches this
-- project's existing "legacy DeviceTypeID kept so old rows still resolve" precedent) - safe to
-- remove in a future cleanup once nothing references them.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: every statement uses IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS `deviceUnitZoneRule` (
  `IDDeviceUnitZoneRule` INT NOT NULL AUTO_INCREMENT,
  `DeviceUnitZoneID` INT NOT NULL,
  -- 1=Ventilation, 2=Light, 3=Heating, 4=WaterPump (api.Models.RelayFunction, same convention as
  -- the old deviceScheduleSlot.RelayFunction and the deviceTypeRelay seed order).
  `RelayFunction` INT NOT NULL,
  -- 1=Threshold, 2=Interval, 3=Schedule (api.Models.ConditionType).
  `ConditionType` INT NOT NULL,
  -- JSON blob (user decision, 2026-09-04) - shape depends on ConditionType, (de)serialized at the
  -- application layer (api.Models.ConditionConfigJson), not a native JSON column type - matches
  -- AgrumyDbContext's provider-neutral "no vendor-specific HasColumnType" principle.
  `ConditionConfig` TEXT NOT NULL,
  PRIMARY KEY (`IDDeviceUnitZoneRule`),
  KEY `ix_deviceUnitZoneRule_zone` (`DeviceUnitZoneID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Roadmap #36: WaterPump-only device-side hard safety limits, moved here from deviceConfigController
-- - NOT a Rule (see deviceUnitZoneRule above), an override ceiling applied by the device AFTER any
-- rule has already decided WaterPump should be on. Seeded from AgrumySettings on zone creation from
-- here on (Agrumy.Api EfRepository.DeviceUnitZoneAddAsync) - existing zone rows stay NULL until an
-- admin sets them (or a new zone is created), same "no backfill, opt-in" pattern as SensorDataRetentionDays.
ALTER TABLE `deviceUnitZone`
  ADD COLUMN IF NOT EXISTS `WaterPumpMaxRunSeconds` INT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `WaterPumpCooldownSeconds` INT DEFAULT NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceUnitZoneRule`;
--   SHOW COLUMNS FROM `deviceUnitZone` LIKE 'WaterPump%';
