using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Tenant facet - lookups and the silent create-on-registration path (see
    /// UserApiController.UserRegistration), plus Tenant Management CRUD (roadmap #196).</summary>
    public interface ITenantRepository
    {
        Task<bool> TenantGetAsync(string tenantName);
        Task<int?> TenantGetIdAsync(string tenantName);
        Task<int> TenantAddAsync(string tenantName);

        Task<IList<Tenant>> TenantsGetAllAsync();
        Task<Tenant?> TenantGetByIdAsync(int idTenant);
        Task TenantUpdateAsync(Tenant tenant);

        /// <summary>Whether TenantID=0 is a safe target for ImportAsSentinel - true
        /// only when it has no devices and at most the single still-unclaimed bootstrap admin row
        /// (PwdHash IS NULL every fresh install seeds - see EfRepository.SeedBootstrapAdminAsync).
        /// Any real device or a second/claimed user means someone is already using this server, so
        /// import-as-sentinel refuses rather than merging into or overwriting them.</summary>
        Task<bool> TenantZeroIsEmptyAsync();

        /// <summary>Every saved WiFi AP for this tenant - roadmap #268 Register's 0/1/many
        /// branching (DiscoveryApiController.Register) decides what to do based on this count.</summary>
        Task<IList<TenantWifiConfig>> TenantWifiConfigsGetAsync(int tenantID);

        Task<TenantWifiConfig> TenantWifiConfigAddAsync(TenantWifiConfig config);
    }
}
