using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;

namespace api.Security
{
    /// <summary>
    /// Wraps the cookie scheme's default ticket format so an "authorization" cookie that fails to
    /// decrypt is treated as "no ticket" (normal anonymous request, [Authorize] redirects to
    /// /Login) instead of crashing the request. ASP.NET Core's CookieAuthenticationHandler does not
    /// catch a failed Unprotect itself - a CryptographicException from it propagates unhandled
    /// straight through UseAuthentication() into the generic error page (dotnet/aspnetcore#2520,
    /// #39807 - well-known, not specific to this app). Hit for real on admin.agrumy.com 2026-09-01:
    /// every "authorization" cookie issued before roadmap #79's DataProtection:KeyPath fix was
    /// signed with a since-lost ephemeral (in-memory-only) key, so every request carrying one of
    /// those pre-#79 cookies threw on every subsequent restart until the browser obtained a fresh
    /// cookie from the persisted key ring. The same failure mode recurs any time a signing key is
    /// no longer in the ring for any reason (rotation, a wiped keys directory, a multi-instance
    /// deploy with inconsistent key material) - this makes ANY such case degrade gracefully instead
    /// of reproducing that incident.
    /// </summary>
    public sealed class SafeTicketDataFormat(
        ISecureDataFormat<AuthenticationTicket> inner, ILogger<SafeTicketDataFormat> logger)
        : ISecureDataFormat<AuthenticationTicket>
    {
        public string Protect(AuthenticationTicket data) => inner.Protect(data);
        public string Protect(AuthenticationTicket data, string? purpose) => inner.Protect(data, purpose);

        public AuthenticationTicket? Unprotect(string? protectedText) => TryUnprotect(() => inner.Unprotect(protectedText));
        public AuthenticationTicket? Unprotect(string? protectedText, string? purpose) => TryUnprotect(() => inner.Unprotect(protectedText, purpose));

        private AuthenticationTicket? TryUnprotect(Func<AuthenticationTicket?> unprotect)
        {
            try
            {
                return unprotect();
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Discarding an authorization cookie that failed to decrypt - " +
                    "treating the request as anonymous instead of letting it crash.");
                return null;
            }
        }
    }
}
