using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IAuditLogRepository members - forwarded to the standalone EfAuditLogRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task AuditLogAddAsync(AuditLogEntry entry) => auditLogRepository.AuditLogAddAsync(entry);

        public Task<IReadOnlyList<AuditLogEntry>> AuditLogGetAsync(int? tenantId, int take = 200, string? actorEmail = null, string? action = null, string? targetType = null, DateTime? fromUtc = null, DateTime? toUtc = null) =>
            auditLogRepository.AuditLogGetAsync(tenantId, take, actorEmail, action, targetType, fromUtc, toUtc);
    }
}
