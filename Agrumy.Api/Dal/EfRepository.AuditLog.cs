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

        public async Task<IReadOnlyList<AuditLogEntry>> AuditLogGetAsync(int? tenantId, int take = 200, string? actorEmail = null, string? action = null, string? targetType = null, DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            IQueryable<AuditLogRow> query = db.AuditLogs.AsNoTracking();
            if (tenantId is int id)
            {
                query = query.Where(a => a.TenantID == id);
            }
#pragma warning disable CA1862 // StringComparison overloads of Contains aren't translatable by EF Core to SQL - ToLower() is the portable way to get a case-insensitive LIKE.
            if (!string.IsNullOrWhiteSpace(actorEmail))
            {
                string term = actorEmail.ToLower();
                query = query.Where(a => a.ActorEmail != null && a.ActorEmail.ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(action))
            {
                string term = action.ToLower();
                query = query.Where(a => a.Action.ToLower().Contains(term));
            }
#pragma warning restore CA1862
            if (!string.IsNullOrWhiteSpace(targetType))
            {
                query = query.Where(a => a.TargetType == targetType);
            }
            if (fromUtc is DateTime from)
            {
                query = query.Where(a => a.TimestampUtc >= from);
            }
            if (toUtc is DateTime to)
            {
                query = query.Where(a => a.TimestampUtc <= to);
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
