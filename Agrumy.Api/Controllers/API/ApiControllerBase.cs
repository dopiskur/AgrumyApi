using api.Dal.Interface;
using api.Models;
using api.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace api.Controllers.API
{
    /// Shared base for the JSON API controllers - cache injection and caller identity off the JWT; <see cref="api.Filters.DbExceptionFilter"/> turns data-access exceptions into responses so actions don't catch them individually. Only takes the two facets WriteAuditAsync itself needs - controllers inject whichever wider/narrower repository facets their own actions call, not through this base.
    [ApiController]
    [ApiVersion("1.0")]
    public abstract class ApiControllerBase : ControllerBase
    {
        private readonly IUserRepository userRepo;
        private readonly IAuditLogRepository auditLogRepo;
        protected ICache Cache { get; }

        protected ApiControllerBase(IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache)
        {
            this.userRepo = userRepo;
            this.auditLogRepo = auditLogRepo;
            Cache = cache;
        }

        /// TenantID claim set at login (JwtTokenProvider.CreateToken), or null if absent.
        protected int? CallerTenantId
        {
            get
            {
                var claim = (User.Identity as ClaimsIdentity)?.FindFirst("TenantID");
                return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
            }
        }

        /// The caller's FIRST role claim, kept so legacy "admin"/"user" checks keep working (JwtTokenProvider.CreateToken always adds that alias first) - new code should prefer <see cref="CallerHasRole"/> or <see cref="CallerRoles"/> instead, since a caller can hold several roles.
        protected string? CallerRole => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Role)?.Value;

        /// Every role claim on the caller's token.
        protected IEnumerable<string> CallerRoles =>
            (User.Identity as ClaimsIdentity)?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();

        /// True if the caller holds this exact role name, among possibly several.
        protected bool CallerHasRole(string roleName) => CallerRoles.Contains(roleName);

        // Legacy fallback: an account missed by the multi-role migration has only the old admin/user claim - treat tenant-0 admin as global, any other admin as tenant-wide.

        /// Token carries none of the current role names - only the legacy "admin"/"user" claim.
        private bool LegacyOnlyToken => !CallerRoles.Any(r => RoleNames.All.Contains(r));

        private bool LegacyAdminFallback => LegacyOnlyToken && CallerHasRole(RoleNames.LegacyAdmin);

        protected bool CallerIsGlobalAdmin =>
            CallerHasRole(RoleNames.GlobalAdmin) || (LegacyAdminFallback && CallerTenantId == 0);

        protected bool CallerManagesUsersGlobally =>
            CallerIsGlobalAdmin || CallerHasRole(RoleNames.GlobalUser);

        /// May the caller create/edit/delete users belonging to <paramref name="targetTenantId"/>.
        protected bool CallerManagesUsers(int? targetTenantId) =>
            CallerManagesUsersGlobally ||
            ((CallerHasRole(RoleNames.TenantAdmin) || CallerHasRole(RoleNames.TenantUser) || LegacyAdminFallback)
             && targetTenantId == CallerTenantId);

        /// Beyond CallerManagesUsers' tenant check: may the caller act on a user holding <paramref name="targetRoleNames"/>, given relative privilege - Global admin outranks everyone; a Global User grant outranks everyone except a Global admin; Tenant admin outranks everyone in-tenant; a plain Tenant User grant outranks everyone in-tenant except a Tenant admin.
        protected bool CallerOutranksTarget(IEnumerable<string> targetRoleNames)
        {
            ICollection<string> targetRoles = targetRoleNames as ICollection<string> ?? targetRoleNames.ToList();
            if (targetRoles.Contains(RoleNames.GlobalAdmin))
            {
                return CallerIsGlobalAdmin;
            }
            if (CallerManagesUsersGlobally)
            {
                return true;
            }
            if (CallerHasRole(RoleNames.TenantAdmin) || LegacyAdminFallback)
            {
                return true;
            }
            return !targetRoles.Contains(RoleNames.TenantAdmin);
        }

        protected bool CallerManagesDevicesGlobally =>
            CallerIsGlobalAdmin || CallerHasRole(RoleNames.GlobalDevice);

        /// May the caller modify/delete devices belonging to <paramref name="targetTenantId"/>.
        protected bool CallerManagesDevices(int? targetTenantId) =>
            CallerManagesDevicesGlobally ||
            ((CallerHasRole(RoleNames.TenantAdmin) || CallerHasRole(RoleNames.TenantDevice) || LegacyAdminFallback)
             && targetTenantId == CallerTenantId);

        // Reads: managing implies reading; Global reader reads everything everywhere but writes nothing.
        protected bool CallerReadsUsersGlobally =>
            CallerManagesUsersGlobally || CallerHasRole(RoleNames.GlobalReader);

        protected bool CallerReadsDevicesGlobally =>
            CallerManagesDevicesGlobally || CallerHasRole(RoleNames.GlobalReader);

        /// True only when a Data Reader grant is the caller's SOLE access - device configuration/rules and user accounts stay hidden from that narrow, sensor-data-and-metrics-only role, but never from anyone who also holds a broader role.
        protected bool CallerIsDataReaderOnly =>
            (CallerHasRole(RoleNames.GlobalDataReader) || CallerHasRole(RoleNames.TenantDataReader))
            && !CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader) && !CallerManagesDevicesGlobally
            && !CallerHasRole(RoleNames.TenantAdmin) && !CallerHasRole(RoleNames.TenantReader) && !CallerHasRole(RoleNames.TenantDevice)
            && !CallerManagesUsersGlobally && !CallerHasRole(RoleNames.TenantUser);

        /// Shared body behind each Device-domain controller's per-entity EnsureOwned* helper: 404 on missing, 403 on tenant mismatch unless the caller's role crosses tenants (CallerManagesDevicesGlobally on a write, the wider CallerReadsDevicesGlobally on a read).
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

        /// Looks up the caller's IDUser by their JWT email rather than trusting a claim, since the token carries no user-id claim.
        protected async Task WriteAuditAsync(string action, int? targetTenantId, string targetType, string targetId, string? details)
        {
            string? actorEmail = User.Identity?.Name;
            User? actor = string.IsNullOrEmpty(actorEmail) ? null : await userRepo.UserGetAsync(null, actorEmail, null);
            await auditLogRepo.AuditLogAddAsync(new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                TenantID = targetTenantId,
                ActorUserID = actor?.IDUser,
                ActorEmail = actorEmail,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Details = details,
            });
        }
    }
}
