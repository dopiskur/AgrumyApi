using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IDeviceUnitRepository members - forwarded to the standalone EfDeviceUnitRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<IList<DeviceUnit>> DeviceUnitsGetAsync(int? tenantID) => deviceUnitRepository.DeviceUnitsGetAsync(tenantID);

        public Task<DeviceUnit?> DeviceUnitGetByIdAsync(int? idDeviceUnit) => deviceUnitRepository.DeviceUnitGetByIdAsync(idDeviceUnit);

        public Task<DeviceUnit> DeviceUnitAddAsync(DeviceUnit unit) => deviceUnitRepository.DeviceUnitAddAsync(unit);

        public Task DeviceUnitUpdateAsync(DeviceUnit unit) => deviceUnitRepository.DeviceUnitUpdateAsync(unit);

        public Task DeviceUnitDeleteAsync(int idDeviceUnit) => deviceUnitRepository.DeviceUnitDeleteAsync(idDeviceUnit);

        public Task<IList<DeviceUnitZone>> DeviceUnitZonesGetAsync(int idDeviceUnit) => deviceUnitRepository.DeviceUnitZonesGetAsync(idDeviceUnit);

        public Task<DeviceUnitZone?> DeviceUnitZoneGetByIdAsync(int? idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone);

        public Task<DeviceUnitZone> DeviceUnitZoneAddAsync(DeviceUnitZone zone) => deviceUnitRepository.DeviceUnitZoneAddAsync(zone);

        public Task DeviceUnitZoneUpdateAsync(DeviceUnitZone zone) => deviceUnitRepository.DeviceUnitZoneUpdateAsync(zone);

        public Task DeviceUnitZoneConfigVersionBumpAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneConfigVersionBumpAsync(idDeviceUnitZone);

        public Task DeviceUnitZoneDeleteAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneDeleteAsync(idDeviceUnitZone);

        public Task<IList<DeviceUnitZoneRule>> RulesGetForZoneAsync(int idDeviceUnitZone) => deviceUnitRepository.RulesGetForZoneAsync(idDeviceUnitZone);

        public Task<IList<DeviceUnitZoneRule>> RulesGetForUnitAsync(int idDeviceUnit) => deviceUnitRepository.RulesGetForUnitAsync(idDeviceUnit);

        public Task<IList<DeviceUnitZoneRule>> RulesGetForTenantGlobalAsync(int tenantId) => deviceUnitRepository.RulesGetForTenantGlobalAsync(tenantId);

        public Task<IList<DeviceUnitZoneRule>> RulesGetNotificationRulesForTenantAsync(int tenantId) => deviceUnitRepository.RulesGetNotificationRulesForTenantAsync(tenantId);

        public Task<DeviceUnitZoneRule?> RuleGetByIdAsync(int? idRule) => deviceUnitRepository.RuleGetByIdAsync(idRule);

        public Task<int> RuleAddAsync(DeviceUnitZoneRule rule) => deviceUnitRepository.RuleAddAsync(rule);

        public Task<IList<DeviceUnitZoneRule>> RulesReferencingAsync(int ruleId, int tenantId) => deviceUnitRepository.RulesReferencingAsync(ruleId, tenantId);

        public Task RuleDeleteAsync(int idRule) => deviceUnitRepository.RuleDeleteAsync(idRule);

        public Task<bool> RuleNotificationWasTrueGetAsync(int ruleId, int idDeviceUnitZone) => deviceUnitRepository.RuleNotificationWasTrueGetAsync(ruleId, idDeviceUnitZone);

        public Task RuleNotificationWasTrueSetAsync(int ruleId, int idDeviceUnitZone, bool wasTrue, DateTime? lastFiredAtUtc) =>
            deviceUnitRepository.RuleNotificationWasTrueSetAsync(ruleId, idDeviceUnitZone, wasTrue, lastFiredAtUtc);

        public Task<bool> DeviceUnitZoneHasControllerAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneHasControllerAsync(idDeviceUnitZone);

        public Task<Device?> DeviceUnitZoneGetControllerAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneGetControllerAsync(idDeviceUnitZone);

        public Task<IList<Device>> DeviceUnitGetControllersAsync(int idDeviceUnit) => deviceUnitRepository.DeviceUnitGetControllersAsync(idDeviceUnit);

        public Task<IList<Device>> DeviceUnitZoneGetSensorsAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneGetSensorsAsync(idDeviceUnitZone);

        public Task<IList<Device>> DeviceUnitGetSensorsAsync(int idDeviceUnit) => deviceUnitRepository.DeviceUnitGetSensorsAsync(idDeviceUnit);

        public Task<IList<Device>> DeviceUnassignedGetAsync(int? tenantID, bool controllerCapable) => deviceUnitRepository.DeviceUnassignedGetAsync(tenantID, controllerCapable);

        public Task DeviceAssignToZoneAsync(int idDevice, int idDeviceUnitZone) => deviceUnitRepository.DeviceAssignToZoneAsync(idDevice, idDeviceUnitZone);

        public Task DeviceUnassignFromZoneAsync(int idDevice) => deviceUnitRepository.DeviceUnassignFromZoneAsync(idDevice);

        public Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGetAsync(int? tenantID) => deviceUnitRepository.DeviceUnitDashboardGetAsync(tenantID);

        public Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGetAsync(int idDeviceUnit) => deviceUnitRepository.DeviceUnitZoneDashboardListGetAsync(idDeviceUnit);

        public Task<DeviceUnitZoneDashboard?> DeviceUnitZoneDashboardGetAsync(int idDeviceUnitZone) => deviceUnitRepository.DeviceUnitZoneDashboardGetAsync(idDeviceUnitZone);

        public Task<IList<TankRefillAlertCandidate>> TankRefillAlertCandidatesGetAsync() => deviceUnitRepository.TankRefillAlertCandidatesGetAsync();

        public Task TankRefillNotifiedSetAsync(int idDeviceUnitZone, DateTime? notifiedAt) => deviceUnitRepository.TankRefillNotifiedSetAsync(idDeviceUnitZone, notifiedAt);

        public Task ManualOverrideStartAsync(DeviceManualOverride manualOverride) => deviceUnitRepository.ManualOverrideStartAsync(manualOverride);

        public Task ManualOverrideStopAsync(int deviceId, RelayFunction relayFunction) => deviceUnitRepository.ManualOverrideStopAsync(deviceId, relayFunction);

        public Task<IList<DeviceManualOverride>> ManualOverridesActiveForDeviceAsync(int deviceId) => deviceUnitRepository.ManualOverridesActiveForDeviceAsync(deviceId);
    }
}
