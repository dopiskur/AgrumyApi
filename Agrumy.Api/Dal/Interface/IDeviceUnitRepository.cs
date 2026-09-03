using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Unit/Zone facet of the data layer: CRUD, device assignment, and the hierarchical
    /// dashboard aggregation. Split out from IDeviceRepository since this is a sizeable domain of
    /// its own, not a couple of extra members on an already-large facet.</summary>
    public interface IDeviceUnitRepository
    {
        // ---- Unit CRUD -------------------------------------------------

        /// <summary>Every real Unit in the tenant (or every tenant when tenantID is null - callers
        /// must check CallerReadsDevicesGlobally themselves, same rule as DevicesGetAllAsync). Never
        /// includes the IDDeviceUnit=0 "Default" sentinel - that is not a real, admin-manageable Unit.</summary>
        Task<IList<DeviceUnit>> DeviceUnitsGetAsync(int? tenantID);

        /// <summary>The Unit with this id (no tenant filter) - for ownership checks before an
        /// authorized write, same pattern as IDeviceRepository.DeviceGetByIdAsync - or null if none.</summary>
        Task<DeviceUnit?> DeviceUnitGetByIdAsync(int? idDeviceUnit);

        Task<DeviceUnit> DeviceUnitAddAsync(DeviceUnit unit);

        Task DeviceUnitUpdateAsync(DeviceUnit unit);

        /// <summary>Cascade-deletes every Zone under this Unit first (each zone's own devices are
        /// unassigned exactly like DeviceUnassignFromZoneAsync - pure bookkeeping, no config-sync),
        /// then the Unit row itself. A no-op if the id does not exist.</summary>
        Task DeviceUnitDeleteAsync(int idDeviceUnit);

        // ---- Zone CRUD ------------------------------------------------

        /// <summary>Every Zone belonging to this Unit. Never includes the IDDeviceUnitZone=0
        /// "Disabled" sentinel.</summary>
        Task<IList<DeviceUnitZone>> DeviceUnitZonesGetAsync(int idDeviceUnit);

        /// <summary>The Zone with this id (no tenant filter) - for ownership checks - or null if none.</summary>
        Task<DeviceUnitZone?> DeviceUnitZoneGetByIdAsync(int? idDeviceUnitZone);

        Task<DeviceUnitZone> DeviceUnitZoneAddAsync(DeviceUnitZone zone);

        Task DeviceUnitZoneUpdateAsync(DeviceUnitZone zone);

        /// <summary>Unassigns every device currently in this Zone (pure bookkeeping, same as
        /// DeviceUnassignFromZoneAsync), then deletes the Zone row. A no-op if the id does not exist.</summary>
        Task DeviceUnitZoneDeleteAsync(int idDeviceUnitZone);

        /// <summary>Whether this Zone already has a controller-capable device assigned - a Zone has
        /// at most one controller. Checked by the API before DeviceAssignToZoneAsync when the
        /// device being assigned is itself controller-capable.</summary>
        Task<bool> DeviceUnitZoneHasControllerAsync(int idDeviceUnitZone);

        /// <summary>The zone's one controller device, or null if the zone has none -
        /// CommandQueueService's Zone-target fan-out resolves to exactly this device, erroring (not
        /// silently no-op-ing) when it's null.</summary>
        Task<Device?> DeviceUnitZoneGetControllerAsync(int idDeviceUnitZone);

        /// <summary>Every controller device across every zone under this unit (zones with no
        /// controller are simply absent from the result, not an error).</summary>
        Task<IList<Device>> DeviceUnitGetControllersAsync(int idDeviceUnit);

        // ---- Device assignment -----------------------------------------

        /// <summary>Every device in the tenant with no current Unit/Zone - the "Add Controller"/"Add
        /// Sensor" picker list. controllerCapable selects DeviceControllerEnabled devices for "Add
        /// Controller", DeviceSensorEnabled devices otherwise - a device with both flags set
        /// appears in both lists but assigning it via either action moves the whole device/row.</summary>
        Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable);

        /// <summary>Assigns one device to one zone - sets both DeviceUnitID (resolved from the
        /// zone's own DeviceUnitID) and DeviceUnitZoneID, and bumps ConfigVersion so the device
        /// picks up its new assignment on its next config poll.</summary>
        Task DeviceAssignToZoneAsync(int idDevice, int idDeviceUnitZone);

        /// <summary>Resets DeviceUnitID/DeviceUnitZoneID to the 0 "unassigned" sentinel - pure
        /// server-side bookkeeping, deliberately does NOT bump ConfigVersion or otherwise notify
        /// the device.</summary>
        Task DeviceUnassignFromZoneAsync(int idDevice);

        // ---- Dashboard aggregation -------------------------------------

        /// <summary>One cube per real Unit in scope (tenantID null = every tenant) - name,
        /// zone/device counts, and the per-sensor-type average across every device in every zone of
        /// that unit.</summary>
        Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGetAsync(int? tenantID);

        /// <summary>One cube per Zone within one Unit - same shape, narrowed scope. Devices list is
        /// left empty (populated only by the single-zone detail below).</summary>
        Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGetAsync(int idDeviceUnit);

        /// <summary>Single-zone detail: roll-up plus the actual device list. Null if the zone id
        /// does not exist.</summary>
        Task<DeviceUnitZoneDashboard?> DeviceUnitZoneDashboardGetAsync(int idDeviceUnitZone);

        // ---- Zone rules -------------------------------------------------

        /// <summary>Every rule belonging to this zone, ordered by RelayFunction. Several rows may
        /// share the same RelayFunction (OR semantics, resolved by whoever evaluates them - the
        /// firmware for config-poll, nothing server-side needs to combine them).</summary>
        Task<IList<DeviceUnitZoneRule>> DeviceUnitZoneRulesGetAsync(int idDeviceUnitZone);

        /// <summary>Single rule by id (no tenant filter) - for ownership checks before an authorized
        /// write (resolve its DeviceUnitZoneID, then check that zone's tenant) - or null if none.</summary>
        Task<DeviceUnitZoneRule?> DeviceUnitZoneRuleGetByIdAsync(int? idDeviceUnitZoneRule);

        Task<int> DeviceUnitZoneRuleAddAsync(DeviceUnitZoneRule rule);

        /// <summary>A no-op if the id does not exist.</summary>
        Task DeviceUnitZoneRuleDeleteAsync(int idDeviceUnitZoneRule);

        /// <summary>Bumps ConfigVersion for every device currently assigned to this zone - called
        /// after any change to the zone's rules or safety limits so the next config poll picks it up.</summary>
        Task DeviceUnitZoneConfigVersionBumpAsync(int idDeviceUnitZone);
    }
}
