using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;

namespace api.Security
{
    // ASP.NET Core's CookieAuthenticationHandler does not catch a failed Unprotect; without this wrapper an undecryptable cookie crashes the request instead of being treated as anonymous.
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
