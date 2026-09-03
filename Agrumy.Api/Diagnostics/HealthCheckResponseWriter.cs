using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace api.Diagnostics
{
    /// <summary>Default MapHealthChecks() output is a bare status string - replaced with per-check detail (name/status/description/duration) so a monitor can tell WHICH dependency is degraded.</summary>
    internal static class HealthCheckResponseWriter
    {
        public static Task WriteResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 2)
                })
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
