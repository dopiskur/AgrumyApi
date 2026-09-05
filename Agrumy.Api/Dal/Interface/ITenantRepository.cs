using api.Models;

namespace api.Dal.Interface
{
    /// Tenant facet - lookups, the silent create-on-registration path (see UserApiController.UserRegistration), and Tenant Management CRUD.
    public interface ITenantRepository
    {
        Task<bool> TenantGetAsync(string tenantName);
        Task<int?> TenantGetIdAsync(string tenantName);
        Task<int> TenantAddAsync(string tenantName);

        Task<IList<Tenant>> TenantsGetAllAsync();
        Task<Tenant?> TenantGetByIdAsync(int idTenant);
        Task TenantUpdateAsync(Tenant tenant);

        /// The only writer of Tenant.EmergencyStopActive (roadmap #230) - also bumps ConfigVersion for every device in the tenant so the change reaches them on their next poll rather than waiting for the heartbeat window.
        Task TenantEmergencyStopSetAsync(int idTenant, bool active);

        /// True only when TenantID=0 has no devices and at most the single still-unclaimed bootstrap admin row (see EfRepository.SeedBootstrapAdminAsync) - any real device or claimed user means ImportAsSentinel must refuse rather than overwrite them.
        Task<bool> TenantZeroIsEmptyAsync();

        /// Every saved WiFi AP for this tenant - DiscoveryApiController.Register's 0/1/many branching decides what to do based on this count.
        Task<IList<TenantWifiConfig>> TenantWifiConfigsGetAsync(int tenantID);

        Task<TenantWifiConfig> TenantWifiConfigAddAsync(TenantWifiConfig config);

        /// No tenant filter - for ownership checks before an authorized write, same pattern as DeviceGetByIdAsync.
        Task<TenantWifiConfig?> TenantWifiConfigGetByIdAsync(int idTenantWifiConfig);

        Task TenantWifiConfigUpdateAsync(TenantWifiConfig config);

        /// A no-op if the id does not exist.
        Task TenantWifiConfigDeleteAsync(int idTenantWifiConfig);
    }
}
