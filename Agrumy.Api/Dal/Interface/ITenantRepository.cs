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

        /// True only when TenantID=0 has no devices and at most the single still-unclaimed bootstrap admin row (see EfRepository.SeedBootstrapAdminAsync) - any real device or claimed user means ImportAsSentinel must refuse rather than overwrite them.
        Task<bool> TenantZeroIsEmptyAsync();
    }
}
