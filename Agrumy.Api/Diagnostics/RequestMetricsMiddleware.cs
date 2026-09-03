using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace api.Diagnostics
{
    /// <summary>Roadmap #143. Must run after UseRouting (so <see cref="HttpContext.GetEndpoint"/>
    /// resolves the matched route pattern instead of the raw, high-cardinality path - e.g.
    /// "/api/Device/{id}" rather than one metrics series per device id) but before authentication/
    /// the endpoint executes, so timing covers the full request including auth/rate-limiting.</summary>
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
