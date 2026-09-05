using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IAuditLogRepository members.
    internal partial class EfRepository
    {
        public async Task AuditLogAddAsync(AuditLogEntry entry)
        {
            db.AuditLogs.Add(new AuditLogRow
            {
                TimestampUtc = entry.TimestampUtc,
                TenantID = entry.TenantID,
                ActorUserID = entry.ActorUserID,
                ActorEmail = entry.ActorEmail,
                Action = entry.Action,
                TargetType = entry.TargetType,
                TargetId = entry.TargetId,
                Details = entry.Details,
            });
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AuditLogEntry>> AuditLogGetAsync(int? tenantId, int take = 200)
        {
            IQueryable<AuditLogRow> query = db.AuditLogs.AsNoTracking();
            if (tenantId is int id)
            {
                query = query.Where(a => a.TenantID == id);
            }

            return await query
                .OrderByDescending(a => a.TimestampUtc)
                .Take(take)
                .Select(a => new AuditLogEntry
                {
                    IDAuditLog = a.IDAuditLog,
                    TimestampUtc = a.TimestampUtc,
                    TenantID = a.TenantID,
                    ActorUserID = a.ActorUserID,
                    ActorEmail = a.ActorEmail,
                    Action = a.Action,
                    TargetType = a.TargetType,
                    TargetId = a.TargetId,
                    Details = a.Details,
                })
                .ToListAsync();
        }
    }
}
