using System.Text.Json;
using System.Text.Json.Nodes;
using api.Commands;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace api.Controllers.API
{
    /// Unit/Zone CRUD, device assignment, and hierarchical dashboard aggregation - ownership checks mirror DeviceApiController.EnsureOwnedDeviceAsync, same CallerReadsDevicesGlobally/CallerManagesDevicesGlobally rules as the rest of the Device domain.
    [Route("/api/DeviceFarmUnit")]
    public class DeviceFarmUnitApiController(IDeviceFarmUnitRepository deviceFarmUnitRepo, IDeviceRepository deviceRepo, IServerConfigRepository serverConfigRepo, IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache, IOptions<AgrumySettings> settingsOptions, ManualActuateService manualActuate) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        // Absolute ceiling - must match AgrumyFirmware DeviceModel.h's MAX_RULES, enforced independently of ServerConfig.MaxRulesPerZone in case a row predates that validation.
        private const int HardMaxRulesPerZone = 32;

        // Absolute ceiling - must match AgrumyFirmware DeviceModel.h's MAX_CONDITIONS_PER_RULE; unlike MaxRulesPerZone this is a per-rule structural limit, not an admin-configurable soft cap.
        private const int HardMaxConditionsPerRule = 8;

        #region Farm CRUD

        [Authorize]
        [HttpGet("Farm/All")]
        public async Task<ActionResult<IList<DeviceFarm>>> DeviceFarmsGet() =>
            Ok(await deviceFarmUnitRepo.DeviceFarmsGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        [Authorize]
        [HttpGet("Farm")]
        public async Task<ActionResult<DeviceFarm>> DeviceFarmGet(int? idDeviceFarm)
        {
            var (farm, error) = await EnsureOwnedFarmAsync(idDeviceFarm, forWrite: false);
            return error ?? Ok(farm);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Farm")]
        public async Task<ActionResult<DeviceFarm>> DeviceFarmAdd([FromBody] DeviceFarm farm)
        {
            farm.TenantID = CallerTenantId; // payload cannot pick another tenant - same rule as every other Add
            DeviceFarm added = await deviceFarmUnitRepo.DeviceFarmAddAsync(farm);
            await WriteAuditAsync("DeviceFarm.Created", added.TenantID, "DeviceFarm", added.IDDeviceFarm.ToString()!, added.DeviceFarmName);
            return Ok(added);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Farm")]
        public async Task<ActionResult<bool>> DeviceFarmUpdate([FromBody] DeviceFarm farm)
        {
            var (existing, error) = await EnsureOwnedFarmAsync(farm.IDDeviceFarm, forWrite: true);
            if (error != null)
            {
                return error;
            }
            farm.TenantID = existing!.TenantID; // payload cannot move a farm to another tenant
            await deviceFarmUnitRepo.DeviceFarmUpdateAsync(farm);
            await WriteAuditAsync("DeviceFarm.Updated", existing.TenantID, "DeviceFarm", existing.IDDeviceFarm.ToString()!, farm.DeviceFarmName);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Farm")]
        public async Task<ActionResult<bool>> DeviceFarmDelete(int? idDeviceFarm)
        {
            var (farm, error) = await EnsureOwnedFarmAsync(idDeviceFarm, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await deviceFarmUnitRepo.DeviceFarmDeleteAsync(farm!.IDDeviceFarm!.Value);
            await WriteAuditAsync("DeviceFarm.Deleted", farm.TenantID, "DeviceFarm", idDeviceFarm.ToString()!, farm.DeviceFarmName);
            return true;
        }

        #endregion

        #region Unit CRUD

        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<IList<DeviceFarmUnit>>> DeviceFarmUnitsGet() =>
            Ok(await deviceFarmUnitRepo.DeviceFarmUnitsGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<DeviceFarmUnit>> DeviceFarmUnitGet(int? idDeviceFarmUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: false);
            return error ?? Ok(unit);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        public async Task<ActionResult<DeviceFarmUnit>> DeviceFarmUnitAdd([FromBody] DeviceFarmUnit unit)
        {
            unit.TenantID = CallerTenantId; // payload cannot pick another tenant - same rule as every other Add
            DeviceFarmUnit added = await deviceFarmUnitRepo.DeviceFarmUnitAddAsync(unit);
            await WriteAuditAsync("DeviceFarmUnit.Created", added.TenantID, "DeviceFarmUnit", added.IDDeviceFarmUnit.ToString()!, added.DeviceFarmUnitName);
            return Ok(added);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceFarmUnitUpdate([FromBody] DeviceFarmUnit unit)
        {
            var (existing, error) = await EnsureOwnedUnitAsync(unit.IDDeviceFarmUnit, forWrite: true);
            if (error != null)
            {
                return error;
            }
            unit.TenantID = existing!.TenantID; // payload cannot move a unit to another tenant
            await deviceFarmUnitRepo.DeviceFarmUnitUpdateAsync(unit);
            await WriteAuditAsync("DeviceFarmUnit.Updated", existing.TenantID, "DeviceFarmUnit", existing.IDDeviceFarmUnit.ToString()!, unit.DeviceFarmUnitName);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete]
        public async Task<ActionResult<bool>> DeviceFarmUnitDelete(int? idDeviceFarmUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await deviceFarmUnitRepo.DeviceFarmUnitDeleteAsync(unit!.IDDeviceFarmUnit!.Value);
            await WriteAuditAsync("DeviceFarmUnit.Deleted", unit.TenantID, "DeviceFarmUnit", idDeviceFarmUnit.ToString()!, unit.DeviceFarmUnitName);
            return true;
        }

        #endregion

        #region Zone CRUD

        /// Every Zone within one Unit - ownership is checked on the Unit, not per-zone, since a zone always belongs to exactly one unit.
        [Authorize]
        [HttpGet("Zone")]
        public async Task<ActionResult<IList<DeviceFarmUnitZone>>> DeviceFarmUnitZonesGet(int? idDeviceFarmUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await deviceFarmUnitRepo.DeviceFarmUnitZonesGetAsync(unit!.IDDeviceFarmUnit!.Value));
        }

        /// Single zone by id, so a caller that needs to patch one field can fetch-then-resubmit the whole object - DeviceFarmUnitZoneUpdateAsync overwrites unconditionally, it does not merge.
        [Authorize]
        [HttpGet("ZoneById")]
        public async Task<ActionResult<DeviceFarmUnitZone>> DeviceFarmUnitZoneGetById(int? idDeviceFarmUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: false);
            return error ?? Ok(zone);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone")]
        public async Task<ActionResult<DeviceFarmUnitZone>> DeviceFarmUnitZoneAdd([FromBody] DeviceFarmUnitZone zone)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(zone.DeviceFarmUnitID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            zone.TenantID = unit!.TenantID; // the owning unit's tenant, not necessarily the caller's (a Global admin may add to another tenant's unit)
            DeviceFarmUnitZone added = await deviceFarmUnitRepo.DeviceFarmUnitZoneAddAsync(zone);
            await WriteAuditAsync("DeviceFarmUnitZone.Created", added.TenantID, "DeviceFarmUnitZone", added.IDDeviceFarmUnitZone.ToString()!, added.DeviceFarmUnitZoneName);
            return Ok(added);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Zone")]
        public async Task<ActionResult<bool>> DeviceFarmUnitZoneUpdate([FromBody] DeviceFarmUnitZone zone)
        {
            var (existing, error) = await EnsureOwnedZoneAsync(zone.IDDeviceFarmUnitZone, forWrite: true);
            if (error != null)
            {
                return error;
            }

            // Catches human error server-side - see api.Utils.SafetyLimitValidation for the shared range.
            if (!SafetyLimitValidation.IsValid(zone.WaterPumpMaxRunSeconds))
            {
                return BadRequest($"WaterPump max run time must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }
            if (!SafetyLimitValidation.IsValid(zone.WaterPumpCooldownSeconds))
            {
                return BadRequest($"WaterPump cooldown must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }
            if (!SafetyLimitValidation.IsValid(zone.HeatingMaxRunSeconds))
            {
                return BadRequest($"Heating max run time must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }
            if (!SafetyLimitValidation.IsValid(zone.VentilationMaxRunSeconds))
            {
                return BadRequest($"Ventilation max run time must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }

            zone.TenantID = existing!.TenantID; // payload cannot move a zone to another tenant
            zone.DeviceFarmUnitID = existing.DeviceFarmUnitID; // ...or to another unit - rename only
            await deviceFarmUnitRepo.DeviceFarmUnitZoneUpdateAsync(zone);
            await WriteAuditAsync("DeviceFarmUnitZone.Updated", existing.TenantID, "DeviceFarmUnitZone", existing.IDDeviceFarmUnitZone.ToString()!, zone.DeviceFarmUnitZoneName);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Zone")]
        public async Task<ActionResult<bool>> DeviceFarmUnitZoneDelete(int? idDeviceFarmUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await deviceFarmUnitRepo.DeviceFarmUnitZoneDeleteAsync(zone!.IDDeviceFarmUnitZone!.Value);
            await WriteAuditAsync("DeviceFarmUnitZone.Deleted", zone.TenantID, "DeviceFarmUnitZone", idDeviceFarmUnitZone.ToString()!, zone.DeviceFarmUnitZoneName);
            return true;
        }

        #endregion

        #region Rules (Zone/Unit/Global scope)

        [Authorize]
        [HttpGet("Zone/Rule")]
        public async Task<ActionResult<IList<DeviceFarmUnitZoneRule>>> DeviceFarmUnitZoneRulesGet(int? idDeviceFarmUnitZone)
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view zone rules.");
            }
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await deviceFarmUnitRepo.RulesGetForZoneAsync(zone!.IDDeviceFarmUnitZone!.Value));
        }

        [Authorize]
        [HttpGet("Unit/Rule")]
        public async Task<ActionResult<IList<DeviceFarmUnitZoneRule>>> DeviceFarmUnitRulesGet(int? idDeviceFarmUnit)
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view unit rules.");
            }
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await deviceFarmUnitRepo.RulesGetForUnitAsync(unit!.IDDeviceFarmUnit!.Value));
        }

        [Authorize]
        [HttpGet("Farm/Rule")]
        public async Task<ActionResult<IList<DeviceFarmUnitZoneRule>>> DeviceFarmRulesGet(int? idDeviceFarm)
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view farm rules.");
            }
            var (farm, error) = await EnsureOwnedFarmAsync(idDeviceFarm, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await deviceFarmUnitRepo.RulesGetForFarmAsync(farm!.IDDeviceFarm!.Value));
        }

        [Authorize]
        [HttpGet("Global/Rule")]
        public async Task<ActionResult<IList<DeviceFarmUnitZoneRule>>> GlobalRulesGet()
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view global rules.");
            }
            if (CallerTenantId is not int tenantId)
            {
                return StatusCode(403, "Caller has no tenant.");
            }
            return Ok(await deviceFarmUnitRepo.RulesGetForTenantGlobalAsync(tenantId));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone/Rule")]
        public async Task<ActionResult<int>> DeviceFarmUnitZoneRuleAdd([FromBody] DeviceFarmUnitZoneRule rule)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(rule.DeviceFarmUnitZoneID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            rule.DeviceFarmUnitID = null;
            rule.DeviceFarmID = null;
            rule.TenantID = zone!.TenantID ?? CallerTenantId ?? 0;
            return await AddRuleAsync(rule, existingCount: (await deviceFarmUnitRepo.RulesGetForZoneAsync(zone.IDDeviceFarmUnitZone!.Value)).Count, scopeLabel: $"zone {rule.DeviceFarmUnitZoneID}");
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Unit/Rule")]
        public async Task<ActionResult<int>> DeviceFarmUnitRuleAdd([FromBody] DeviceFarmUnitZoneRule rule)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(rule.DeviceFarmUnitID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            rule.DeviceFarmUnitZoneID = null;
            rule.DeviceFarmID = null;
            rule.TenantID = unit!.TenantID ?? CallerTenantId ?? 0;
            return await AddRuleAsync(rule, existingCount: (await deviceFarmUnitRepo.RulesGetForUnitAsync(unit.IDDeviceFarmUnit!.Value)).Count, scopeLabel: $"unit {rule.DeviceFarmUnitID}");
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Farm/Rule")]
        public async Task<ActionResult<int>> DeviceFarmRuleAdd([FromBody] DeviceFarmUnitZoneRule rule)
        {
            var (farm, error) = await EnsureOwnedFarmAsync(rule.DeviceFarmID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            rule.DeviceFarmUnitZoneID = null;
            rule.DeviceFarmUnitID = null;
            rule.TenantID = farm!.TenantID ?? CallerTenantId ?? 0;
            return await AddRuleAsync(rule, existingCount: (await deviceFarmUnitRepo.RulesGetForFarmAsync(farm.IDDeviceFarm!.Value)).Count, scopeLabel: $"farm {rule.DeviceFarmID}");
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Global/Rule")]
        public async Task<ActionResult<int>> GlobalRuleAdd([FromBody] DeviceFarmUnitZoneRule rule)
        {
            if (CallerTenantId is not int tenantId)
            {
                return StatusCode(403, "Caller has no tenant.");
            }
            rule.DeviceFarmUnitZoneID = null;
            rule.DeviceFarmUnitID = null;
            rule.DeviceFarmID = null;
            rule.TenantID = tenantId;
            return await AddRuleAsync(rule, existingCount: (await deviceFarmUnitRepo.RulesGetForTenantGlobalAsync(tenantId)).Count, scopeLabel: $"tenant {tenantId} (global)");
        }

        /// Shared validate+cap+persist body for all four scopes - the only difference between them is which EnsureOwned*/existing-count call the caller already made.
        private async Task<ActionResult<int>> AddRuleAsync(DeviceFarmUnitZoneRule rule, int existingCount, string scopeLabel)
        {
            if (await RuleShapeErrorAsync(rule) is string shapeError)
            {
                return BadRequest(shapeError);
            }
            int configuredMax = (await serverConfigRepo.ServerConfigGetAsync(1)).MaxRulesPerZone ?? settings.MaxRulesPerZone;
            int effectiveMax = Math.Min(configuredMax, HardMaxRulesPerZone);
            if (existingCount >= effectiveMax)
            {
                return BadRequest($"This scope already has {existingCount} rules, the configured maximum ({effectiveMax}). Remove one before adding another.");
            }
            int idRule = await deviceFarmUnitRepo.RuleAddAsync(rule);
            await WriteAuditAsync("DeviceFarmUnitZoneRule.Created", rule.TenantID, "DeviceFarmUnitZoneRule", idRule.ToString(), $"{scopeLabel}, {rule.ActionType}/{rule.RelayFunction}{rule.SensorMetric}");
            return Ok(idRule);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Zone/Rule")]
        public Task<ActionResult<bool>> DeviceFarmUnitZoneRuleDelete(int? idDeviceFarmUnitZoneRule) => DeleteRuleAsync(idDeviceFarmUnitZoneRule);

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Unit/Rule")]
        public Task<ActionResult<bool>> DeviceFarmUnitRuleDelete(int? idDeviceFarmUnitZoneRule) => DeleteRuleAsync(idDeviceFarmUnitZoneRule);

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Farm/Rule")]
        public Task<ActionResult<bool>> DeviceFarmRuleDelete(int? idDeviceFarmUnitZoneRule) => DeleteRuleAsync(idDeviceFarmUnitZoneRule);

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Global/Rule")]
        public Task<ActionResult<bool>> GlobalRuleDelete(int? idDeviceFarmUnitZoneRule) => DeleteRuleAsync(idDeviceFarmUnitZoneRule);

        /// One shared delete body regardless of which scope route it came in through - ownership is resolved from the rule's OWN scope fields, not the route.
        private async Task<ActionResult<bool>> DeleteRuleAsync(int? idRule)
        {
            DeviceFarmUnitZoneRule? rule = await deviceFarmUnitRepo.RuleGetByIdAsync(idRule);
            if (rule == null)
            {
                return NotFound();
            }
            var error = await EnsureOwnedRuleAsync(rule, forWrite: true);
            if (error != null)
            {
                return error;
            }
            var referencing = await deviceFarmUnitRepo.RulesReferencingAsync(idRule!.Value, rule.TenantID);
            if (referencing.Count > 0)
            {
                string names = string.Join(", ", referencing.Select(r => $"#{r.IDDeviceFarmUnitZoneRule}"));
                return Conflict($"Cannot delete: still referenced by another rule's \"another rule fired\" condition ({names}). Remove that condition first.");
            }
            await deviceFarmUnitRepo.RuleDeleteAsync(idRule.Value);
            await WriteAuditAsync("DeviceFarmUnitZoneRule.Deleted", rule.TenantID, "DeviceFarmUnitZoneRule", idRule.ToString()!, $"{rule.ActionType}/{rule.RelayFunction}{rule.SensorMetric}");
            return true;
        }

        /// Resolves ownership from the rule's OWN scope (Zone/Unit/Farm/Global), not the caller's route - a rule can only ever have one of those four shapes.
        private async Task<ActionResult?> EnsureOwnedRuleAsync(DeviceFarmUnitZoneRule rule, bool forWrite)
        {
            if (rule.DeviceFarmUnitZoneID is int idZone)
            {
                return (await EnsureOwnedZoneAsync(idZone, forWrite)).Error;
            }
            if (rule.DeviceFarmUnitID is int idUnit)
            {
                return (await EnsureOwnedUnitAsync(idUnit, forWrite)).Error;
            }
            if (rule.DeviceFarmID is int idFarm)
            {
                return (await EnsureOwnedFarmAsync(idFarm, forWrite)).Error;
            }
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            return rule.TenantID != CallerTenantId && !crossTenantAllowed
                ? StatusCode(403, "Rule belongs to a different tenant")
                : null;
        }

        /// Shape+bound check for the whole rule: ActionType/RelayFunction/SensorMetric consistency, condition-list bounds, per-condition shape, and (DB-dependent, hence async) RuleTriggered's cross-reference validity.
        private async Task<string?> RuleShapeErrorAsync(DeviceFarmUnitZoneRule rule)
        {
            if (rule.ActionType == ActionType.Relay)
            {
                if (rule.RelayFunction == null) { return "Relay rule: relayFunction is required."; }
                if (rule.SensorMetric != null) { return "Relay rule: sensorMetric must not be set."; }
            }
            else
            {
                // sensorMetric is required only when a Threshold condition needs to know which reading to check - a pure Interval/Schedule/RuleTriggered reminder rule has nothing to measure and legitimately leaves it null.
                if (rule.SensorMetric == null && rule.Conditions.Any(c => c.ConditionType == ConditionType.Threshold))
                {
                    return "Notification rule: sensorMetric is required when a condition is Threshold.";
                }
                if (rule.RelayFunction != null) { return "Notification rule: relayFunction must not be set."; }
                if (string.IsNullOrWhiteSpace(rule.NotificationSubject)) { return "Notification rule: subject is required."; }
            }

            if (rule.Conditions.Count == 0)
            {
                return "A rule needs at least one condition.";
            }
            if (rule.Conditions.Count > HardMaxConditionsPerRule)
            {
                return $"A rule may have at most {HardMaxConditionsPerRule} conditions.";
            }
            for (int i = 0; i < rule.Conditions.Count; i++)
            {
                RuleCondition condition = rule.Conditions[i];
                bool needsOperator = i > 0;
                if (needsOperator != (condition.Operator != null))
                {
                    return needsOperator
                        ? $"Condition {i + 1}: an AND/OR operator is required (every condition after the first)."
                        : $"Condition {i + 1}: the first condition must not have an operator.";
                }
                if (condition.ConditionType == ConditionType.RuleTriggered && rule.ActionType != ActionType.Notification)
                {
                    return $"Condition {i + 1}: \"another rule fired\" is only valid on a Notification-action rule (a Relay rule fires on-device, invisibly to the server).";
                }
                if (condition.ConditionType == ConditionType.Astronomical && rule.ActionType != ActionType.Relay)
                {
                    return $"Condition {i + 1}: an astronomical condition is only valid on a Relay-action rule (AstronomicalRuleResolver only runs on the Relay path - api.Devices.RuleConditionEvaluator has no case for it, so a Notification rule would always evaluate this condition as false).";
                }
                if (RuleConditionConfigError(condition.ConditionType, condition.ConditionConfig) is string conditionError)
                {
                    return $"Condition {i + 1}: {conditionError}";
                }
                if (condition.ConditionType == ConditionType.RuleTriggered)
                {
                    var config = condition.ConditionConfig?.Deserialize<RuleTriggeredConditionConfig>(ConditionConfigJson.Options);
                    DeviceFarmUnitZoneRule? referenced = config == null ? null : await deviceFarmUnitRepo.RuleGetByIdAsync(config.ReferencedRuleId);
                    if (referenced == null || referenced.TenantID != rule.TenantID || referenced.ActionType != ActionType.Notification)
                    {
                        return $"Condition {i + 1}: referenced rule must exist, belong to the same tenant, and be a Notification-action rule.";
                    }
                }
            }
            return null;
        }

        /// Shape+bound check per ConditionType - the firmware would otherwise silently treat a malformed rule as inert (ConfigParser/evaluateRule), a confusing way to discover a typo; Threshold's own value is deliberately unbounded, only Hysteresis has a universal "must not be negative" rule.
        private static string? RuleConditionConfigError(ConditionType type, JsonNode? config)
        {
            try
            {
                switch (type)
                {
                    case ConditionType.Threshold:
                        var threshold = config.Deserialize<ThresholdConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing threshold config");
                        if (threshold.Hysteresis < 0)
                        {
                            return "hysteresis must not be negative.";
                        }
                        return null;
                    case ConditionType.Interval:
                        var interval = config.Deserialize<IntervalConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing interval config");
                        if (interval.Interval <= 0)
                        {
                            return "interval must be greater than 0.";
                        }
                        if (interval.IntervalLength <= 0 || interval.IntervalLength > interval.Interval)
                        {
                            return "on-duration must be greater than 0 and not exceed the interval.";
                        }
                        return null;
                    case ConditionType.Schedule:
                        var schedule = config.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing schedule config");
                        // DaysOfWeek must fit the 7-bit mask AgrumyFirmware's evaluateCondition expects (bit 0 = Sunday .. bit 6 = Saturday); a window crossing local midnight is not supported.
                        if (schedule.DaysOfWeek < 0 || schedule.DaysOfWeek > 0b1111111)
                        {
                            return "days of week must be a value from 0 to 127.";
                        }
                        if (schedule.Start < 0 || schedule.Start > 86399)
                        {
                            return "start must be between 0 and 86399 seconds since local midnight.";
                        }
                        if (schedule.Duration < 1 || schedule.Start + schedule.Duration > 86400)
                        {
                            return "duration must be at least 1 second and not cross local midnight (start + duration <= 86400).";
                        }
                        return null;
                    case ConditionType.Astronomical:
                        var astro = config.Deserialize<AstronomicalConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing astronomical config");
                        if (astro.DaysOfWeek < 0 || astro.DaysOfWeek > 0b1111111)
                        {
                            return "days of week must be a value from 0 to 127.";
                        }
                        if (astro.SunriseOffsetMinutes < -720 || astro.SunriseOffsetMinutes > 720
                            || astro.SunsetOffsetMinutes < -720 || astro.SunsetOffsetMinutes > 720)
                        {
                            return "offsets must be between -720 and 720 minutes.";
                        }
                        return null;
                    case ConditionType.RuleTriggered:
                        _ = config.Deserialize<RuleTriggeredConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing ruleTriggered config");
                        return null;
                    default:
                        return "unknown condition type.";
                }
            }
            catch (JsonException)
            {
                return $"conditionConfig does not match the expected shape for {type}.";
            }
        }

        #endregion

        #region Manual actuate (roadmap #219)

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone/ManualActuate")]
        public async Task<ActionResult<IReadOnlyList<int>>> ZoneManualActuateStart(int idDeviceFarmUnitZone, [FromBody] ManualActuateRequest request)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: true);
            if (error != null)
            {
                return error;
            }
            ManualActuateResult result = await manualActuate.StartForZoneAsync(idDeviceFarmUnitZone, request);
            if (result.Outcome == ManualActuateOutcome.Success)
            {
                await WriteAuditAsync("DeviceFarmUnitZone.ManualActuateStarted", zone!.TenantID, "DeviceFarmUnitZone", idDeviceFarmUnitZone.ToString(), $"{request.RelayFunction}/{request.Mode}");
            }
            return ManualActuateResponse(result);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Unit/ManualActuate")]
        public async Task<ActionResult<IReadOnlyList<int>>> UnitManualActuateStart(int idDeviceFarmUnit, [FromBody] ManualActuateRequest request)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: true);
            if (error != null)
            {
                return error;
            }
            ManualActuateResult result = await manualActuate.StartForUnitAsync(idDeviceFarmUnit, request);
            if (result.Outcome == ManualActuateOutcome.Success)
            {
                await WriteAuditAsync("DeviceFarmUnit.ManualActuateStarted", unit!.TenantID, "DeviceFarmUnit", idDeviceFarmUnit.ToString(), $"{request.RelayFunction}/{request.Mode}");
            }
            return ManualActuateResponse(result);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone/ManualActuate/Stop")]
        public async Task<ActionResult> ZoneManualActuateStop(int idDeviceFarmUnitZone, RelayFunction relayFunction)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await manualActuate.StopAsync(idDeviceFarmUnitZone, relayFunction);
            await WriteAuditAsync("DeviceFarmUnitZone.ManualActuateStopped", zone!.TenantID, "DeviceFarmUnitZone", idDeviceFarmUnitZone.ToString(), relayFunction.ToString());
            return Ok();
        }

        /// The zone's currently-active manual commands (not yet past ExpiresAtUtc) - what the Web UI polls to render "currently active, X remaining".
        [Authorize]
        [HttpGet("Zone/ManualActuate")]
        public async Task<ActionResult<IList<DeviceManualOverride>>> ZoneManualActuateStatus(int idDeviceFarmUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: false);
            if (error != null)
            {
                return error;
            }
            Device? controller = await deviceFarmUnitRepo.DeviceFarmUnitZoneGetControllerAsync(idDeviceFarmUnitZone);
            if (controller?.IDDevice is not int deviceId)
            {
                return Ok(Array.Empty<DeviceManualOverride>());
            }
            return Ok(await deviceFarmUnitRepo.ManualOverridesActiveForDeviceAsync(deviceId));
        }

        private ActionResult<IReadOnlyList<int>> ManualActuateResponse(ManualActuateResult result) => result.Outcome switch
        {
            ManualActuateOutcome.Success => Ok(result.AffectedDeviceIds),
            ManualActuateOutcome.TargetNotFound => NotFound(result.Message),
            _ => BadRequest(result.Message),
        };

        #endregion

        #region Device assignment

        /// Devices with no current zone, filtered to controller- or sensor-capable - the "Add Controller"/"Add Sensor" picker list.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("Unassigned")]
        public async Task<ActionResult<IList<DeviceDto>>> DeviceUnassignedGet(bool controllerCapable) =>
            Ok((await deviceFarmUnitRepo.DeviceUnassignedGetAsync(CallerManagesDevicesGlobally ? null : CallerTenantId, controllerCapable))
                .Select(d => d.ToDto()).ToList());

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Assign")]
        public async Task<ActionResult<bool>> DeviceAssign([FromBody] DeviceZoneAssignment body)
        {
            var (device, deviceError) = await EnsureOwnedDeviceAsync(
                () => deviceRepo.DeviceGetByIdAsync(body.IDDevice), "Device", forWrite: true);
            if (deviceError != null)
            {
                return deviceError;
            }

            var (zone, zoneError) = await EnsureOwnedZoneAsync(body.IDDeviceFarmUnitZone, forWrite: true);
            if (zoneError != null)
            {
                return zoneError;
            }

            // Unconditional, no exception for a caller who legitimately crosses tenants for the two ownership checks above - a device must never end up assigned into another tenant's zone, not even by a Global admin's mistake.
            if (device!.TenantID != zone!.TenantID)
            {
                return StatusCode(403, "Device and zone belong to different tenants.");
            }

            // A zone has at most one controller (not required, but capped at one).
            if (device!.DeviceControllerEnabled == true && await deviceFarmUnitRepo.DeviceFarmUnitZoneHasControllerAsync(body.IDDeviceFarmUnitZone))
            {
                return Conflict("This zone already has a controller assigned.");
            }

            await deviceFarmUnitRepo.DeviceAssignToZoneAsync(body.IDDevice, body.IDDeviceFarmUnitZone);
            await WriteAuditAsync("Device.AssignedToZone", device.TenantID, "Device", body.IDDevice.ToString(), $"zone {body.IDDeviceFarmUnitZone}");
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Unassign")]
        public async Task<ActionResult<bool>> DeviceUnassign(int? idDevice)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(
                () => deviceRepo.DeviceGetByIdAsync(idDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }
            await deviceFarmUnitRepo.DeviceUnassignFromZoneAsync(device!.IDDevice!.Value);
            await WriteAuditAsync("Device.UnassignedFromZone", device.TenantID, "Device", idDevice.ToString()!, null);
            return true;
        }

        #endregion

        #region Dashboard

        /// Top-level Unit cubes - read-only, open to any authenticated caller (same reasoning as DeviceApiController.DeviceFleetGet).
        [Authorize]
        [HttpGet("Dashboard")]
        public async Task<ActionResult<IList<DeviceFarmUnitDashboard>>> DeviceFarmUnitDashboardGet() =>
            Ok(await deviceFarmUnitRepo.DeviceFarmUnitDashboardGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        [Authorize]
        [HttpGet("Dashboard/Zones")]
        public async Task<ActionResult<IList<DeviceFarmUnitZoneDashboard>>> DeviceFarmUnitZoneDashboardListGet(int? idDeviceFarmUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceFarmUnit, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await deviceFarmUnitRepo.DeviceFarmUnitZoneDashboardListGetAsync(unit!.IDDeviceFarmUnit!.Value));
        }

        [Authorize]
        [HttpGet("Dashboard/Zone")]
        public async Task<ActionResult<DeviceFarmUnitZoneDashboard>> DeviceFarmUnitZoneDashboardGet(int? idDeviceFarmUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceFarmUnitZone, forWrite: false);
            if (error != null)
            {
                return error;
            }
            DeviceFarmUnitZoneDashboard? dashboard = await deviceFarmUnitRepo.DeviceFarmUnitZoneDashboardGetAsync(zone!.IDDeviceFarmUnitZone!.Value);
            return dashboard is null ? NotFound() : Ok(dashboard);
        }

        #endregion

        /// Same shape as DeviceApiController.EnsureOwnedDeviceAsync, for DeviceFarm (roadmap #384).
        private Task<(DeviceFarm? Farm, ActionResult? Error)> EnsureOwnedFarmAsync(int? idDeviceFarm, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => deviceFarmUnitRepo.DeviceFarmGetByIdAsync(idDeviceFarm), f => f.TenantID, "Farm", forWrite);

        /// Same shape as DeviceApiController.EnsureOwnedDeviceAsync, for DeviceFarmUnit - see ApiControllerBase.EnsureOwnedDeviceEntityAsync for the shared 404/403 logic.
        private Task<(DeviceFarmUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int? idDeviceFarmUnit, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => deviceFarmUnitRepo.DeviceFarmUnitGetByIdAsync(idDeviceFarmUnit), u => u.TenantID, "Unit", forWrite);

        /// Same shape as EnsureOwnedUnitAsync, for DeviceFarmUnitZone.
        private Task<(DeviceFarmUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int? idDeviceFarmUnitZone, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => deviceFarmUnitRepo.DeviceFarmUnitZoneGetByIdAsync(idDeviceFarmUnitZone), z => z.TenantID, "Zone", forWrite);

        /// Same shape as EnsureOwnedUnitAsync, for Device.
        private Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(
            Func<Task<Device?>> lookup, string ownerLabel, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(lookup, d => d.TenantID, ownerLabel, forWrite);
    }
}
