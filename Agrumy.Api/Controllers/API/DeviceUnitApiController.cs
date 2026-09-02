using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Roadmap #82 (Unit/Zone CRUD + device assignment) and #81 (hierarchical dashboard
    /// aggregation). Ownership checks mirror DeviceApiController.EnsureOwnedDeviceAsync - a
    /// tenant-scoped caller only sees/writes its own tenant's Units/Zones, a Global
    /// admin/Device/reader crosses tenants per the same CallerReadsDevicesGlobally/
    /// CallerManagesDevicesGlobally rules as the rest of the Device domain.</summary>
    [Route("/api/DeviceUnit")]
    public class DeviceUnitApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
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

        /// <summary>Every Zone within one Unit - ownership is checked on the Unit, not per-zone,
        /// since a zone always belongs to exactly one unit and cannot be listed without one.</summary>
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

        #region Device assignment (roadmap #82)

        /// <summary>Devices with no current zone, filtered to controller- or sensor-capable - the
        /// "Add Controller"/"Add Sensor" picker list (#82 rule (d)).</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("Unassigned")]
        public async Task<ActionResult<IList<Device>>> DeviceUnassignedGet(bool controllerCapable) =>
            Ok(await Repo.DeviceUnassignedGetAsync(CallerManagesDevicesGlobally ? null : CallerTenantId, controllerCapable));

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

            // #82 rule (a): a zone has at most one controller (not required, but capped at one).
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

        #region Dashboard (roadmap #81)

        /// <summary>Top-level Unit cubes - read-only, open to any authenticated caller (same
        /// reasoning as DeviceApiController.DeviceFleetGet).</summary>
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

        /// <summary>Same shape as DeviceApiController.EnsureOwnedDeviceAsync, for DeviceUnit.</summary>
        private async Task<(DeviceUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int? idDeviceUnit, bool forWrite)
        {
            DeviceUnit? unit = await Repo.DeviceUnitGetByIdAsync(idDeviceUnit);
            if (unit is null)
            {
                return (null, NotFound());
            }
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (unit.TenantID != CallerTenantId && !crossTenantAllowed)
            {
                return (unit, StatusCode(403, "Unit belongs to a different tenant"));
            }
            return (unit, null);
        }

        /// <summary>Same shape as EnsureOwnedUnitAsync, for DeviceUnitZone.</summary>
        private async Task<(DeviceUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int? idDeviceUnitZone, bool forWrite)
        {
            DeviceUnitZone? zone = await Repo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone);
            if (zone is null)
            {
                return (null, NotFound());
            }
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (zone.TenantID != CallerTenantId && !crossTenantAllowed)
            {
                return (zone, StatusCode(403, "Zone belongs to a different tenant"));
            }
            return (zone, null);
        }

        /// <summary>Same shape/logic as DeviceApiController.EnsureOwnedDeviceAsync (duplicated, not
        /// shared - that one is private to DeviceApiController, matching this codebase's existing
        /// per-controller convention rather than introducing a new shared base for one caller).</summary>
        private async Task<(Device? Device, ActionResult? Error)> EnsureOwnedDeviceAsync(
            Func<Task<Device?>> lookup, string ownerLabel, bool forWrite)
        {
            Device? device = await lookup();
            if (device is null)
            {
                return (null, NotFound());
            }
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (device.TenantID != CallerTenantId && !crossTenantAllowed)
            {
                return (device, StatusCode(403, $"{ownerLabel} belongs to a different tenant"));
            }
            return (device, null);
        }
    }
}
