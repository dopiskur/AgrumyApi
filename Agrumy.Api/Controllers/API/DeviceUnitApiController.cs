using System.Text.Json;
using System.Text.Json.Nodes;
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
    [Route("/api/DeviceUnit")]
    public class DeviceUnitApiController(IRepository repo, ICache cache, IOptions<AgrumySettings> settingsOptions) : ApiControllerBase(repo, cache)
    {
        private readonly AgrumySettings settings = settingsOptions.Value;

        // Absolute ceiling - must match AgrumyFirmware DeviceModel.h's MAX_RULES, enforced independently of ServerConfig.MaxRulesPerZone in case a row predates that validation.
        private const int HardMaxRulesPerZone = 32;

        #region Unit CRUD

        [Authorize]
        [HttpGet("All")]
        public async Task<ActionResult<IList<DeviceUnit>>> DeviceUnitsGet() =>
            Ok(await Repo.DeviceUnitsGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<DeviceUnit>> DeviceUnitGet(int? idDeviceUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceUnit, forWrite: false);
            return error ?? Ok(unit);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost]
        public async Task<ActionResult<DeviceUnit>> DeviceUnitAdd([FromBody] DeviceUnit unit)
        {
            unit.TenantID = CallerTenantId; // payload cannot pick another tenant - same rule as every other Add
            return Ok(await Repo.DeviceUnitAddAsync(unit));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut]
        public async Task<ActionResult<bool>> DeviceUnitUpdate([FromBody] DeviceUnit unit)
        {
            var (existing, error) = await EnsureOwnedUnitAsync(unit.IDDeviceUnit, forWrite: true);
            if (error != null)
            {
                return error;
            }
            unit.TenantID = existing!.TenantID; // payload cannot move a unit to another tenant
            await Repo.DeviceUnitUpdateAsync(unit);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete]
        public async Task<ActionResult<bool>> DeviceUnitDelete(int? idDeviceUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceUnit, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await Repo.DeviceUnitDeleteAsync(unit!.IDDeviceUnit!.Value);
            return true;
        }

        #endregion

        #region Zone CRUD

        /// Every Zone within one Unit - ownership is checked on the Unit, not per-zone, since a zone always belongs to exactly one unit.
        [Authorize]
        [HttpGet("Zone")]
        public async Task<ActionResult<IList<DeviceUnitZone>>> DeviceUnitZonesGet(int? idDeviceUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceUnit, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await Repo.DeviceUnitZonesGetAsync(unit!.IDDeviceUnit!.Value));
        }

        /// Single zone by id, so a caller that needs to patch one field can fetch-then-resubmit the whole object - DeviceUnitZoneUpdateAsync overwrites unconditionally, it does not merge.
        [Authorize]
        [HttpGet("ZoneById")]
        public async Task<ActionResult<DeviceUnitZone>> DeviceUnitZoneGetById(int? idDeviceUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceUnitZone, forWrite: false);
            return error ?? Ok(zone);
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone")]
        public async Task<ActionResult<DeviceUnitZone>> DeviceUnitZoneAdd([FromBody] DeviceUnitZone zone)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(zone.DeviceUnitID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            zone.TenantID = unit!.TenantID; // the owning unit's tenant, not necessarily the caller's (a Global admin may add to another tenant's unit)
            return Ok(await Repo.DeviceUnitZoneAddAsync(zone));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPut("Zone")]
        public async Task<ActionResult<bool>> DeviceUnitZoneUpdate([FromBody] DeviceUnitZone zone)
        {
            var (existing, error) = await EnsureOwnedZoneAsync(zone.IDDeviceUnitZone, forWrite: true);
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

            zone.TenantID = existing!.TenantID; // payload cannot move a zone to another tenant
            zone.DeviceUnitID = existing.DeviceUnitID; // ...or to another unit - rename only
            await Repo.DeviceUnitZoneUpdateAsync(zone);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Zone")]
        public async Task<ActionResult<bool>> DeviceUnitZoneDelete(int? idDeviceUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceUnitZone, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await Repo.DeviceUnitZoneDeleteAsync(zone!.IDDeviceUnitZone!.Value);
            return true;
        }

        #endregion

        #region Zone Rules

        [Authorize]
        [HttpGet("Zone/Rule")]
        public async Task<ActionResult<IList<DeviceUnitZoneRule>>> DeviceUnitZoneRulesGet(int? idDeviceUnitZone)
        {
            if (CallerIsDataReaderOnly)
            {
                return StatusCode(403, "Data Reader role cannot view zone rules.");
            }
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceUnitZone, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await Repo.DeviceUnitZoneRulesGetAsync(zone!.IDDeviceUnitZone!.Value));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Zone/Rule")]
        public async Task<ActionResult<int>> DeviceUnitZoneRuleAdd([FromBody] DeviceUnitZoneRule rule)
        {
            var (_, error) = await EnsureOwnedZoneAsync(rule.DeviceUnitZoneID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            if (RuleConditionConfigError(rule.ConditionType, rule.ConditionConfig) is string configError)
            {
                return BadRequest(configError);
            }
            int configuredMax = (await Repo.ServerConfigGetAsync(1)).MaxRulesPerZone ?? settings.MaxRulesPerZone;
            int effectiveMax = Math.Min(configuredMax, HardMaxRulesPerZone);
            int existingRuleCount = (await Repo.DeviceUnitZoneRulesGetAsync(rule.DeviceUnitZoneID)).Count;
            if (existingRuleCount >= effectiveMax)
            {
                return BadRequest($"This zone already has {existingRuleCount} rules, the configured maximum ({effectiveMax}). Remove one before adding another.");
            }
            return Ok(await Repo.DeviceUnitZoneRuleAddAsync(rule));
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpDelete("Zone/Rule")]
        public async Task<ActionResult<bool>> DeviceUnitZoneRuleDelete(int? idDeviceUnitZoneRule)
        {
            DeviceUnitZoneRule? rule = await Repo.DeviceUnitZoneRuleGetByIdAsync(idDeviceUnitZoneRule);
            if (rule == null)
            {
                return NotFound();
            }
            var (_, error) = await EnsureOwnedZoneAsync(rule.DeviceUnitZoneID, forWrite: true);
            if (error != null)
            {
                return error;
            }
            await Repo.DeviceUnitZoneRuleDeleteAsync(idDeviceUnitZoneRule!.Value);
            return true;
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
                            return "Threshold rule: hysteresis must not be negative.";
                        }
                        return null;
                    case ConditionType.Interval:
                        var interval = config.Deserialize<IntervalConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing interval config");
                        if (interval.Interval <= 0)
                        {
                            return "Interval rule: interval must be greater than 0.";
                        }
                        if (interval.IntervalLength <= 0 || interval.IntervalLength > interval.Interval)
                        {
                            return "Interval rule: on-duration must be greater than 0 and not exceed the interval.";
                        }
                        return null;
                    case ConditionType.Schedule:
                        var schedule = config.Deserialize<ScheduleConditionConfig>(ConditionConfigJson.Options)
                            ?? throw new JsonException("missing schedule config");
                        // DaysOfWeek must fit the 7-bit mask AgrumyFirmware's evaluateRule expects (bit 0 = Sunday .. bit 6 = Saturday); a window crossing local midnight is not supported.
                        if (schedule.DaysOfWeek < 0 || schedule.DaysOfWeek > 0b1111111)
                        {
                            return "Schedule rule: days of week must be a value from 0 to 127.";
                        }
                        if (schedule.Start < 0 || schedule.Start > 86399)
                        {
                            return "Schedule rule: start must be between 0 and 86399 seconds since local midnight.";
                        }
                        if (schedule.Duration < 1 || schedule.Start + schedule.Duration > 86400)
                        {
                            return "Schedule rule: duration must be at least 1 second and not cross local midnight (start + duration <= 86400).";
                        }
                        return null;
                    default:
                        return "Unknown condition type.";
                }
            }
            catch (JsonException)
            {
                return $"{type} rule: conditionConfig does not match the expected shape for this condition type.";
            }
        }

        #endregion

        #region Device assignment

        /// Devices with no current zone, filtered to controller- or sensor-capable - the "Add Controller"/"Add Sensor" picker list.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("Unassigned")]
        public async Task<ActionResult<IList<DeviceDto>>> DeviceUnassignedGet(bool controllerCapable) =>
            Ok((await Repo.DeviceUnassignedGetAsync(CallerManagesDevicesGlobally ? null : CallerTenantId, controllerCapable))
                .Select(d => d.ToDto()).ToList());

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Assign")]
        public async Task<ActionResult<bool>> DeviceAssign([FromBody] DeviceZoneAssignment body)
        {
            var (device, deviceError) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(body.IDDevice), "Device", forWrite: true);
            if (deviceError != null)
            {
                return deviceError;
            }

            var (_, zoneError) = await EnsureOwnedZoneAsync(body.IDDeviceUnitZone, forWrite: true);
            if (zoneError != null)
            {
                return zoneError;
            }

            // A zone has at most one controller (not required, but capped at one).
            if (device!.DeviceControllerEnabled == true && await Repo.DeviceUnitZoneHasControllerAsync(body.IDDeviceUnitZone))
            {
                return Conflict("This zone already has a controller assigned.");
            }

            await Repo.DeviceAssignToZoneAsync(body.IDDevice, body.IDDeviceUnitZone);
            return true;
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Unassign")]
        public async Task<ActionResult<bool>> DeviceUnassign(int? idDevice)
        {
            var (device, error) = await EnsureOwnedDeviceAsync(
                () => Repo.DeviceGetByIdAsync(idDevice), "Device", forWrite: true);
            if (error != null)
            {
                return error;
            }
            await Repo.DeviceUnassignFromZoneAsync(device!.IDDevice!.Value);
            return true;
        }

        #endregion

        #region Dashboard

        /// Top-level Unit cubes - read-only, open to any authenticated caller (same reasoning as DeviceApiController.DeviceFleetGet).
        [Authorize]
        [HttpGet("Dashboard")]
        public async Task<ActionResult<IList<DeviceUnitDashboard>>> DeviceUnitDashboardGet() =>
            Ok(await Repo.DeviceUnitDashboardGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId));

        [Authorize]
        [HttpGet("Dashboard/Zones")]
        public async Task<ActionResult<IList<DeviceUnitZoneDashboard>>> DeviceUnitZoneDashboardListGet(int? idDeviceUnit)
        {
            var (unit, error) = await EnsureOwnedUnitAsync(idDeviceUnit, forWrite: false);
            if (error != null)
            {
                return error;
            }
            return Ok(await Repo.DeviceUnitZoneDashboardListGetAsync(unit!.IDDeviceUnit!.Value));
        }

        [Authorize]
        [HttpGet("Dashboard/Zone")]
        public async Task<ActionResult<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardGet(int? idDeviceUnitZone)
        {
            var (zone, error) = await EnsureOwnedZoneAsync(idDeviceUnitZone, forWrite: false);
            if (error != null)
            {
                return error;
            }
            DeviceUnitZoneDashboard? dashboard = await Repo.DeviceUnitZoneDashboardGetAsync(zone!.IDDeviceUnitZone!.Value);
            return dashboard is null ? NotFound() : Ok(dashboard);
        }

        #endregion

        /// Same shape as DeviceApiController.EnsureOwnedDeviceAsync, for DeviceUnit - see ApiControllerBase.EnsureOwnedDeviceEntityAsync for the shared 404/403 logic.
        private Task<(DeviceUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int? idDeviceUnit, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitGetByIdAsync(idDeviceUnit), u => u.TenantID, "Unit", forWrite);

        /// Same shape as EnsureOwnedUnitAsync, for DeviceUnitZone.
        private Task<(DeviceUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int? idDeviceUnitZone, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone), z => z.TenantID, "Zone", forWrite);

        /// Same shape as EnsureOwnedUnitAsync, for Device.
        private Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(
            Func<Task<Device?>> lookup, string ownerLabel, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(lookup, d => d.TenantID, ownerLabel, forWrite);
    }
}
