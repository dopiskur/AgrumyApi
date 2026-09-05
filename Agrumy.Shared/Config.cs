using Microsoft.Extensions.Configuration;

namespace api
{
    /// Static shim for <see cref="Security.JwtTokenProvider"/> only (static by design, shared across Agrumy.Api/Agrumy.Web); everything else should use injected IOptions&lt;AgrumySettings&gt; instead. Init() must run once per host's Program.cs.
    public static class Config
    {
        public static string? secureKey { get; private set; }
        public static string? jwtIssuer { get; private set; }
        public static string? jwtAudience { get; private set; }

        public static void Init(IConfiguration configuration)
        {
            secureKey = configuration.GetSection("JWT:SecureKey").Value;
            jwtIssuer = configuration.GetSection("JWT:Issuer").Value;
            jwtAudience = configuration.GetSection("JWT:Audience").Value;
        }
    }
}
