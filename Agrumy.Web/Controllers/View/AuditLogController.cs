using System.Text;
using api.Dal.Interface;
using api.Security;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Read-only - the API itself already scopes the result to the caller's tenant (or every tenant for a Global admin), nothing further to decide here.
    [Authorize(Roles = RoleNames.Admins)]
    public class AuditLogController(IApi api) : Controller
    {
        public async Task<ActionResult> Index(string? actorEmail, string? action, string? targetType, DateTime? fromUtc, DateTime? toUtc) => View(new AuditLogViewModel
        {
            Entries = await api.AuditLogGet(actorEmail: actorEmail, action: action, targetType: targetType, fromUtc: fromUtc, toUtc: toUtc),
            ActorEmail = actorEmail,
            Action = action,
            TargetType = targetType,
            FromUtc = fromUtc,
            ToUtc = toUtc,
        });

        /// Same filters as Index - exported set always matches what the admin is currently looking at.
        public async Task<ActionResult> ExportCsv(string? actorEmail, string? action, string? targetType, DateTime? fromUtc, DateTime? toUtc)
        {
            var entries = await api.AuditLogGet(actorEmail: actorEmail, action: action, targetType: targetType, fromUtc: fromUtc, toUtc: toUtc);

            var csv = new StringBuilder();
            csv.AppendLine("Time (UTC),Actor,Action,Target Type,Target Id,Details");
            foreach (var entry in entries)
            {
                csv.AppendLine(string.Join(',',
                    CsvField(entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvField(entry.ActorEmail),
                    CsvField(entry.Action),
                    CsvField(entry.TargetType),
                    CsvField(entry.TargetId),
                    CsvField(entry.Details)));
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"audit-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        }

        private static string CsvField(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    }
}
