using api.Models;

namespace api.Dal.Interface
{
    /// The minimal shape TankRefillAlertEvaluator needs - WaterLevel is the zone's latest-per-device reading averaged (same shape as SensorAverages.WaterLevel), null when no device in the zone has reported one.
    public sealed record TankRefillAlertCandidate(
        int IDDeviceUnitZone,
        int TenantID,
        string? DeviceUnitZoneName,
        double? WaterLevel,
        int? WaterLevelRawEmpty,
        int? WaterLevelRawFull,
        double? TankCapacityLiters,
        DateTime? TankRefillNotifiedAt);

    /// Unit/Zone facet of the data layer: CRUD, device assignment, and the hierarchical dashboard aggregation - split out from IDeviceRepository as its own sizeable domain.
    public interface IDeviceUnitRepository
    {
        // ---- Unit CRUD -------------------------------------------------

        /// Every real Unit in the tenant, or every tenant when tenantID is null (caller must check CallerReadsDevicesGlobally) - never includes the IDDeviceUnit=0 "Default" sentinel.
        Task<IList<DeviceUnit>> DeviceUnitsGetAsync(int? tenantID);

        /// The Unit with this id (no tenant filter), for ownership checks before an authorized write - same pattern as IDeviceRepository.DeviceGetByIdAsync - or null if none.
        Task<DeviceUnit?> DeviceUnitGetByIdAsync(int? idDeviceUnit);

        Task<DeviceUnit> DeviceUnitAddAsync(DeviceUnit unit);

        Task DeviceUnitUpdateAsync(DeviceUnit unit);

        /// Cascade-deletes every Zone under this Unit first (devices unassigned via DeviceUnassignFromZoneAsync), then the Unit row - a no-op if the id doesn't exist.
        Task DeviceUnitDeleteAsync(int idDeviceUnit);

        // ---- Zone CRUD ------------------------------------------------

        /// Every Zone belonging to this Unit - never includes the IDDeviceUnitZone=0 "Disabled" sentinel.
        Task<IList<DeviceUnitZone>> DeviceUnitZonesGetAsync(int idDeviceUnit);

        /// The Zone with this id (no tenant filter) - for ownership checks - or null if none.
        Task<DeviceUnitZone?> DeviceUnitZoneGetByIdAsync(int? idDeviceUnitZone);

        Task<DeviceUnitZone> DeviceUnitZoneAddAsync(DeviceUnitZone zone);

        Task DeviceUnitZoneUpdateAsync(DeviceUnitZone zone);

        /// Unassigns every device currently in this Zone (via DeviceUnassignFromZoneAsync), then deletes the Zone row - a no-op if the id doesn't exist.
        Task DeviceUnitZoneDeleteAsync(int idDeviceUnitZone);

        /// Whether this Zone already has a controller-capable device assigned - a Zone has at most one controller.
        Task<bool> DeviceUnitZoneHasControllerAsync(int idDeviceUnitZone);

        /// The zone's one controller device, or null if none - CommandQueueService's Zone-target fan-out errors (does not silently no-op) when this is null.
        Task<Device?> DeviceUnitZoneGetControllerAsync(int idDeviceUnitZone);

        /// Every controller device across every zone under this unit - zones with no controller are simply absent, not an error.
        Task<IList<Device>> DeviceUnitGetControllersAsync(int idDeviceUnit);

        /// Every sensor-only device (DeviceSensorEnabled, not DeviceControllerEnabled) in this zone.
        Task<IList<Device>> DeviceUnitZoneGetSensorsAsync(int idDeviceUnitZone);

        /// Every sensor-only device across every zone under this unit.
        Task<IList<Device>> DeviceUnitGetSensorsAsync(int idDeviceUnit);

        // ---- Device assignment -----------------------------------------

        /// The "Add Controller"/"Add Sensor" picker list: every unassigned device in the tenant, filtered by DeviceControllerEnabled or DeviceSensorEnabled per controllerCapable.
        Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable);

        /// Assigns one device to one zone (sets DeviceUnitID from the zone's own, plus DeviceUnitZoneID) and bumps ConfigVersion so the device picks it up on its next poll.
        Task DeviceAssignToZoneAsync(int idDevice, int idDeviceUnitZone);

        /// Resets DeviceUnitID/DeviceUnitZoneID to NULL ("unassigned") - deliberately does NOT bump ConfigVersion or otherwise notify the device.
        Task DeviceUnassignFromZoneAsync(int idDevice);

        // ---- Dashboard aggregation -------------------------------------

        /// One cube per real Unit in scope (tenantID null = every tenant): name, zone/device counts, and the per-sensor-type average across the unit.
        Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGetAsync(int? tenantID);

        /// One cube per Zone within one Unit, same shape narrowed in scope - Devices list stays empty, populated only by the single-zone detail below.
        Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGetAsync(int idDeviceUnit);

        /// Single-zone detail: roll-up plus the actual device list, null if the zone id doesn't exist.
        Task<DeviceUnitZoneDashboard?> DeviceUnitZoneDashboardGetAsync(int idDeviceUnitZone);

        // ---- Rules (Zone/Unit/Global scope) ------------------------------

        /// Every rule scoped to exactly this zone - several rows may share the same RelayFunction/SensorMetric (OR semantics; Relay-action OR is resolved by the firmware, Notification-action OR by RuleNotificationEvaluator).
        Task<IList<DeviceUnitZoneRule>> RulesGetForZoneAsync(int idDeviceUnitZone);

        /// Every rule scoped to exactly this unit (Unit scope, not the union of its zones' own rules).
        Task<IList<DeviceUnitZoneRule>> RulesGetForUnitAsync(int idDeviceUnit);

        /// Every rule at Global (per-tenant) scope - applies to every unit/zone the tenant owns unless a more specific scope overrides it for that function/metric.
        Task<IList<DeviceUnitZoneRule>> RulesGetForTenantGlobalAsync(int tenantId);

        /// Every Notification-action rule for the tenant across all three scopes, unresolved (RuleNotificationEvaluator does its own per-zone Zone>Unit>Global resolution).
        Task<IList<DeviceUnitZoneRule>> RulesGetNotificationRulesForTenantAsync(int tenantId);

        /// Single rule by id (no tenant filter) - for ownership checks, resolve its scope then check that scope's tenant - or null if none.
        Task<DeviceUnitZoneRule?> RuleGetByIdAsync(int? idRule);

        Task<int> RuleAddAsync(DeviceUnitZoneRule rule);

        /// Every Notification-action rule in the tenant with a RuleTriggered condition referencing ruleId.
        Task<IList<DeviceUnitZoneRule>> RulesReferencingAsync(int ruleId, int tenantId);

        /// A no-op if the id does not exist. Callers must check RulesReferencingAsync first and refuse to delete a still-referenced rule - this method itself does not guard that.
        Task RuleDeleteAsync(int idRule);

        /// Bumps ConfigVersion for every device assigned to this zone - called after any rules/safety-limit change so the next poll picks it up.
        Task DeviceUnitZoneConfigVersionBumpAsync(int idDeviceUnitZone);

        // ---- Notification rule evaluation state --------------------------

        /// False (not just missing) for a (rule, zone) pair with no row yet.
        Task<bool> RuleNotificationWasTrueGetAsync(int ruleId, int idDeviceUnitZone);

        Task RuleNotificationWasTrueSetAsync(int ruleId, int idDeviceUnitZone, bool wasTrue, DateTime? lastFiredAtUtc);

        // ---- Tank refill alert (roadmap #234) --------------------------

        /// Every real, tank-calibrated zone (TankCapacityLiters + both raw calibration points set) across every tenant, with its latest averaged WaterLevel reading.
        Task<IList<TankRefillAlertCandidate>> TankRefillAlertCandidatesGetAsync();

        Task TankRefillNotifiedSetAsync(int idDeviceUnitZone, DateTime? notifiedAt);

        // ---- Manual actuate (roadmap #219) --------------------------

        /// Upserts on (DeviceID, RelayFunction) - starting a new command for an already-active function replaces it, same "restart the timer" semantics as re-triggering anything else in this system.
        Task ManualOverrideStartAsync(DeviceManualOverride manualOverride);

        /// A no-op if none is active for (deviceId, relayFunction).
        Task ManualOverrideStopAsync(int deviceId, RelayFunction relayFunction);

        /// Every override for this device not yet past ExpiresAtUtc - what DeviceConfigBuilder sends on the next poll and the Web UI shows as "currently active".
        Task<IList<DeviceManualOverride>> ManualOverridesActiveForDeviceAsync(int deviceId);
    }
}
