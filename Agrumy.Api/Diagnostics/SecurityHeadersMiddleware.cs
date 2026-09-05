namespace api.Diagnostics
{
    /// Sets response headers with no per-route variation, so plain response mutation is enough - runs early, before routing.
    internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            // unsafe-inline on script/style is a deliberate trade-off: SwaggerUI's own bundle relies on inline script, tightening further needs a nonce-based rewrite of that page specifically.
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

            await next(context);
        }
    }
}
