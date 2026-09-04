namespace api.Security
{
    /// <summary>Sets response headers with no per-route variation, so plain response mutation (not endpoint-specific) is enough - runs early, before routing.</summary>
    internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            // unsafe-inline on script/style: several Views ship page-specific inline <script> blocks, tightening to nonces needs touching each one individually.
            // img-src allows OpenStreetMap tile subdomains - the ServerConfig location picker's Leaflet map loads tiles from there.
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://*.tile.openstreetmap.org; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

            await next(context);
        }
    }
}
