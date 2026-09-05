using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace api.Diagnostics
{
    /// Must run after UseRouting (so <c>GetEndpoint()</c> resolves the route pattern, not one series per device id) but before the endpoint executes, so timing covers the full request.
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
