-- Roadmap #212: rule engine v2 - a rule becomes a flat, left-to-right AND/OR list of conditions
-- (was exactly one condition per rule row), gains a second action type (Notification, evaluated
-- server-side by api.BackgroundWorkers.RuleNotificationEvaluator, alongside the existing implicit
-- Relay action evaluated on-device), and a three-level Global(per-tenant)->Unit->Zone scope
-- hierarchy resolved server-side (api.Devices.RuleHierarchyResolver) - more-specific-scope wins,
-- same pattern as CSS cascade/Group Policy. See agrumy-roadmap-done.md's #212 entry for the full
-- design writeup.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- NOT SAFE TO RE-RUN: data migration + column drop is inherently one-shot, same rationale as
-- 2026-09-05-relay-slot-table-309.sql - a partial-failure re-run (columns added, not yet dropped)
-- is fine (the ADD COLUMNs are IF NOT EXISTS, the backfill UPDATEs are scoped to
-- WHERE Conditions IS NULL), but re-running the whole file after a successful run fails loudly at
-- the backfill UPDATE (ConditionType/ConditionConfig already gone) rather than doing anything harmful.

ALTER TABLE `deviceUnitZoneRule`
  ADD COLUMN IF NOT EXISTS `TenantID` INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `DeviceUnitID` INT DEFAULT NULL,
  -- 1=Relay (existing implicit behavior), 2=Notification (api.Models.ActionType) - every existing
  -- row predates Notification, so it backfills to Relay via the column default.
  ADD COLUMN IF NOT EXISTS `ActionType` INT NOT NULL DEFAULT 1,
  -- Notification-action only (api.Models.SensorMetric) - a Relay rule's metric/direction stays
  -- implicit in RelayFunction, unchanged from #21.
  ADD COLUMN IF NOT EXISTS `SensorMetric` INT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `NotificationSubject` TEXT DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `NotificationBody` TEXT DEFAULT NULL,
  -- JSON array of RuleCondition (api.Models.ConditionConfigJson), replacing the old single
  -- ConditionType+ConditionConfig columns below - see the backfill UPDATE for the 1:1 migration of
  -- an existing row into a single-entry array with a null operator.
  ADD COLUMN IF NOT EXISTS `Conditions` TEXT DEFAULT NULL;

-- Every existing rule predates Unit/Global scope, so it's always Zone-scoped - resolve its
-- TenantID by walking zone -> unit, same denormalization DeviceUnitZoneRow itself already uses.
UPDATE `deviceUnitZoneRule` r
  JOIN `deviceUnitZone` z ON z.IDDeviceUnitZone = r.DeviceUnitZoneID
  JOIN `deviceUnit` u ON u.IDDeviceUnit = z.DeviceUnitID
SET r.TenantID = COALESCE(u.TenantID, 0)
WHERE r.Conditions IS NULL;

-- Built via string concatenation, not JSON_OBJECT(...CAST(x AS JSON)...) - MariaDB has no CAST-to-JSON
-- (JSON there is just a LONGTEXT alias), and ConditionConfig is already valid JSON text, so splicing
-- it in raw is both portable and correct.
UPDATE `deviceUnitZoneRule`
SET Conditions = CONCAT('[{"conditionType":', ConditionType, ',"conditionConfig":', ConditionConfig, ',"operator":null}]')
WHERE Conditions IS NULL;

ALTER TABLE `deviceUnitZoneRule`
  MODIFY COLUMN `Conditions` TEXT NOT NULL,
  -- Nullable now that Unit/Global-scope rules (DeviceUnitZoneID NULL) exist alongside Zone-scope ones.
  MODIFY COLUMN `DeviceUnitZoneID` INT DEFAULT NULL,
  -- Nullable now that a Notification-action rule has no relay to target (api.Models.ActionType) - stays required only for ActionType.Relay, enforced in DeviceUnitApiController, not the DB.
  MODIFY COLUMN `RelayFunction` INT DEFAULT NULL,
  DROP COLUMN `ConditionType`,
  DROP COLUMN `ConditionConfig`,
  ADD INDEX IF NOT EXISTS `ix_deviceUnitZoneRule_unit` (`DeviceUnitID`),
  ADD INDEX IF NOT EXISTS `ix_deviceUnitZoneRule_tenant` (`TenantID`);

-- Per-(rule, zone) dedup latch for RuleNotificationEvaluator - a Unit/Global-scope Notification
-- rule is evaluated independently against every zone it reaches, so "already notified, don't
-- re-fire every tick" state is keyed per zone, not just per rule.
CREATE TABLE IF NOT EXISTS `ruleNotificationState` (
  `IDRuleNotificationState` INT NOT NULL AUTO_INCREMENT,
  `RuleID` INT NOT NULL,
  `DeviceUnitZoneID` INT NOT NULL,
  `WasTrue` TINYINT(1) NOT NULL DEFAULT 0,
  `LastFiredAtUtc` DATETIME DEFAULT NULL,
  PRIMARY KEY (`IDRuleNotificationState`),
  UNIQUE KEY `ux_ruleNotificationState_rule_zone` (`RuleID`, `DeviceUnitZoneID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW COLUMNS FROM `deviceUnitZoneRule`;
--   SELECT COUNT(*) FROM `deviceUnitZoneRule` WHERE Conditions IS NULL; -- expect 0
--   SHOW CREATE TABLE `ruleNotificationState`;
