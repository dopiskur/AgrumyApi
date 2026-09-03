using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace api.Diagnostics
{
    /// <summary>Must run after UseRouting (so <c>HttpContext.GetEndpoint()</c> resolves the matched route pattern, e.g. "/api/Device/{id}", not one series per device id) but before the endpoint executes, so timing covers the full request.</summary>
    internal sealed class RequestMetricsMiddleware(RequestDelegate next, AgrumyMetrics metrics)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await next(context);
            }
            finally
            {
                stopwatch.Stop();
                string route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "unknown";
                metrics.RecordRequest(route, context.Request.Method, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }
}
