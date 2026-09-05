using api.Models;

namespace api.Dal.Interface
{
    /// Write-once admin-action trail - who changed another account's access/state, when, and to what.
    public interface IAuditLogRepository
    {
        Task AuditLogAddAsync(AuditLogEntry entry);

        /// Newest first, capped by take - tenantId null lists every tenant, callers must only pass null for a Global admin since this method does no authorization of its own.
        Task<IReadOnlyList<AuditLogEntry>> AuditLogGetAsync(int? tenantId, int take = 200);
    }
}
