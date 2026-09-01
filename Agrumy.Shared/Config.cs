using Microsoft.Extensions.Configuration;

namespace api
{
    /// <summary>
    /// Roadmap #104: thin static shim, kept only because <see cref="Security.JwtTokenProvider"/> is
    /// static by design and shared verbatim between two independent host processes (Agrumy.Api and
    /// Agrumy.Web each load this assembly separately) - a constructor-injected settings object can't
    /// reach static methods. Everything else should prefer constructor-injected
    /// <c>IOptions&lt;AgrumySettings&gt;</c> instead (see EfRepository, roadmap #101).
    ///
    /// <see cref="Init"/> MUST be called exactly once at startup by EACH host's Program.cs, passing
    /// THAT host's own <c>builder.Configuration</c> - before this, Config built its own
    /// <c>ConfigurationBuilder</c> from <c>Directory.GetCurrentDirectory()/appsettings.json</c>,
    /// entirely outside the host pipeline: no <c>appsettings.{Environment}.json</c> override, no
    /// standard env-var/user-secrets provider (the AGRUMY_DB_PROVIDER env var was a manual,
    /// one-off workaround for exactly this gap), never reloadable, and fragile under systemd where
    /// the working directory does not necessarily match the bin directory.
    /// </summary>
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
