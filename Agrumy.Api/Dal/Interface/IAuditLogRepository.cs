using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Write-once admin-action trail - who changed another account's access/state, when, and to what.</summary>
    public interface IAuditLogRepository
    {
        Task AuditLogAddAsync(AuditLogEntry entry);

        /// <summary>Newest first, capped by take. tenantId null lists every tenant - callers must
        /// only pass null for a Global admin, this method does no authorization of its own.</summary>
        Task<IReadOnlyList<AuditLogEntry>> AuditLogGetAsync(int? tenantId, int take = 200);
    }
}
