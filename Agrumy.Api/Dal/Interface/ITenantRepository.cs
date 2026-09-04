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
    }
}
