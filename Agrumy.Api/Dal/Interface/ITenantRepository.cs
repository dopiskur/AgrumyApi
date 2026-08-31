namespace api.Dal.Interface
{
    /// <summary>Tenant facet (roadmap #74) - lookups and the silent create-on-registration path
    /// (see UserApiController.UserRegistration and roadmap #64).</summary>
    public interface ITenantRepository
    {
        Task<bool> TenantGetAsync(string tenantName);
        Task<int?> TenantGetIdAsync(string tenantName);
        Task<int> TenantAddAsync(string tenantName);
    }
}
