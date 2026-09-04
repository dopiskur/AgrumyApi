using api.Dal.Interface;
using api.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace api.Controllers.API
{
    /// <summary>
    /// Shared base for the JSON API controllers: constructor-injected data-access + cache, and the
    /// caller's identity read off the JWT. Data-access exceptions are turned into responses by the
    /// global <see cref="api.Filters.DbExceptionFilter"/>, so actions don't catch them individually.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected IRepository Repo { get; }
        protected ICache Cache { get; }

        protected ApiControllerBase(IRepository repo, ICache cache)
        {
            Repo = repo;
            Cache = cache;
        }

        /// <summary>TenantID claim set at login (JwtTokenProvider.CreateToken), or null if absent.</summary>
        protected int? CallerTenantId
        {
            get
            {
                var claim = (User.Identity as ClaimsIdentity)?.FindFirst("TenantID");
                return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
            }
        }

        /// <summary>The caller's FIRST role claim - legacy code checking against literal "admin"/"user" keeps working because JwtTokenProvider.CreateToken always adds the legacy alias claim first. New code should prefer <see cref="CallerHasRole"/> or <see cref="CallerRoles"/> instead - a caller can hold several roles at once.</summary>
        protected string? CallerRole => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Role)?.Value;

        /// <summary>Every role claim on the caller's token.</summary>
        protected IEnumerable<string> CallerRoles =>
            (User.Identity as ClaimsIdentity)?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();

        /// <summary>True if the caller holds this exact role name, among possibly several.</summary>
        protected bool CallerHasRole(string roleName) => CallerRoles.Contains(roleName);

        // Every [Authorize(Roles=...)] attribute is only the coarse gate; these helpers make the precise decision (which tenant, read vs write, users vs devices). An account the multi-role migration missed carries only the legacy "admin"/"user" claim - for those, fall back to legacy semantics: tenant-0 admin acted globally, any other admin tenant-wide.

        /// <summary>Token carries none of the current role names - only the legacy "admin"/"user" claim.</summary>
        private bool LegacyOnlyToken => !CallerRoles.Any(r => RoleNames.All.Contains(r));

        private bool LegacyAdminFallback => LegacyOnlyToken && CallerHasRole(RoleNames.LegacyAdmin);

        protected bool CallerIsGlobalAdmin =>
            CallerHasRole(RoleNames.GlobalAdmin) || (LegacyAdminFallback && CallerTenantId == 0);

        protected bool CallerManagesUsersGlobally =>
            CallerIsGlobalAdmin || CallerHasRole(RoleNames.GlobalUser);

        /// <summary>May the caller create/edit/delete users belonging to <paramref name="targetTenantId"/>.</summary>
        protected bool CallerManagesUsers(int? targetTenantId) =>
            CallerManagesUsersGlobally ||
            ((CallerHasRole(RoleNames.TenantAdmin) || CallerHasRole(RoleNames.TenantUser) || LegacyAdminFallback)
             && targetTenantId == CallerTenantId);

        protected bool CallerManagesDevicesGlobally =>
            CallerIsGlobalAdmin || CallerHasRole(RoleNames.GlobalDevice);

        /// <summary>May the caller modify/delete devices belonging to <paramref name="targetTenantId"/>.</summary>
        protected bool CallerManagesDevices(int? targetTenantId) =>
            CallerManagesDevicesGlobally ||
            ((CallerHasRole(RoleNames.TenantAdmin) || CallerHasRole(RoleNames.TenantDevice) || LegacyAdminFallback)
             && targetTenantId == CallerTenantId);

        // Reads: managing implies reading; Global reader reads everything everywhere but writes nothing.
        protected bool CallerReadsUsersGlobally =>
            CallerManagesUsersGlobally || CallerHasRole(RoleNames.GlobalReader);

        protected bool CallerReadsDevicesGlobally =>
            CallerManagesDevicesGlobally || CallerHasRole(RoleNames.GlobalReader);

        /// <summary>Shared body behind each Device-domain controller's per-entity EnsureOwned* helper: 404 on missing, 403 on tenant mismatch unless the caller's role crosses tenants (CallerManagesDevicesGlobally on a write, the wider CallerReadsDevicesGlobally on a read).</summary>
        protected async Task<(T? Entity, ActionResult? Error)> EnsureOwnedDeviceEntityAsync<T>(Func<Task<T?>> lookup, Func<T, int?> tenantIdOf, string ownerLabel, bool forWrite) where T : class
        {
            T? entity = await lookup();
            if (entity is null)
            {
                return (null, NotFound());
            }
            bool crossTenantAllowed = forWrite ? CallerManagesDevicesGlobally : CallerReadsDevicesGlobally;
            if (tenantIdOf(entity) != CallerTenantId && !crossTenantAllowed)
            {
                return (entity, StatusCode(403, $"{ownerLabel} belongs to a different tenant"));
            }
            return (entity, null);
        }
    }
}
