using System.Security.Claims;

namespace api.Security
{
    public static class UserClaims
    {
        public const string TimeZone = "TimeZone";

        /// Cached at sign-in so pages don't round-trip to Agrumy.Api just to convert a UTC timestamp for display.
        public static string? GetTimeZone(this ClaimsPrincipal principal) => principal.FindFirst(TimeZone)?.Value;
    }
}
