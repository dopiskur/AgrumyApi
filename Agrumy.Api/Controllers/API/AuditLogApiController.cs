using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// Read-only view of the admin-action trail written by AuditLogRepository.AuditLogAddAsync - a Global admin sees every tenant's history, a Tenant admin only their own.
    [Route("api/AuditLog")]
    public class AuditLogApiController(IAuditLogRepository auditLogRepo, IUserRepository userRepo, ICache cache) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        // Separate field, not the primary-constructor parameter directly - a parameter used both here and in the base(...) call trips CS9107 (ambiguous double-capture).
        private readonly IAuditLogRepository auditLog = auditLogRepo;
        private const int MaxTake = 500;

        [Authorize(Roles = RoleNames.Admins)]
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<AuditLogEntry>>> AuditLogGet(int take = 200)
        {
            int? tenantId = CallerIsGlobalAdmin ? null : CallerTenantId;
            return Ok(await auditLog.AuditLogGetAsync(tenantId, Math.Clamp(take, 1, MaxTake)));
        }
    }
}
