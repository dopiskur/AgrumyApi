using api.Models;

namespace api.Dal
{
    /// ITenantRepository members - forwarded to the standalone EfTenantRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<bool> TenantGetAsync(string tenantName) => tenantRepository.TenantGetAsync(tenantName);

        public Task<int?> TenantGetIdAsync(string tenantName) => tenantRepository.TenantGetIdAsync(tenantName);

        public Task<int> TenantAddAsync(string tenantName) => tenantRepository.TenantAddAsync(tenantName);

        public Task<IList<Tenant>> TenantsGetAllAsync() => tenantRepository.TenantsGetAllAsync();

        public Task<Tenant?> TenantGetByIdAsync(int idTenant) => tenantRepository.TenantGetByIdAsync(idTenant);

        public Task TenantUpdateAsync(Tenant tenant) => tenantRepository.TenantUpdateAsync(tenant);

        public Task TenantEmergencyStopSetAsync(int idTenant, bool active) => tenantRepository.TenantEmergencyStopSetAsync(idTenant, active);

        public Task<bool> TenantZeroIsEmptyAsync() => tenantRepository.TenantZeroIsEmptyAsync();

        public Task<IList<TenantWifiConfig>> TenantWifiConfigsGetAsync(int tenantID) => tenantRepository.TenantWifiConfigsGetAsync(tenantID);

        public Task<TenantWifiConfig> TenantWifiConfigAddAsync(TenantWifiConfig config) => tenantRepository.TenantWifiConfigAddAsync(config);

        public Task<TenantWifiConfig?> TenantWifiConfigGetByIdAsync(int idTenantWifiConfig) => tenantRepository.TenantWifiConfigGetByIdAsync(idTenantWifiConfig);

        public Task TenantWifiConfigUpdateAsync(TenantWifiConfig config) => tenantRepository.TenantWifiConfigUpdateAsync(config);

        public Task TenantWifiConfigDeleteAsync(int idTenantWifiConfig) => tenantRepository.TenantWifiConfigDeleteAsync(idTenantWifiConfig);
    }
}
