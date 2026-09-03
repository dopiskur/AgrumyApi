using Microsoft.Extensions.Configuration;

namespace api
{
    /// <summary>Thin static shim, kept only because <see cref="Security.JwtTokenProvider"/> is static by design and shared between two independent host processes (Agrumy.Api and Agrumy.Web each load this assembly separately); everything else should prefer constructor-injected <c>IOptions&lt;AgrumySettings&gt;</c> instead. <see cref="Init"/> MUST be called exactly once at startup by EACH host's Program.cs, passing that host's own <c>builder.Configuration</c>.</summary>
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
