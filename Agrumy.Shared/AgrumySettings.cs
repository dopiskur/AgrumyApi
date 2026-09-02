using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace api
{
    /// <summary>
    /// Roadmap #104: replaces the values <see cref="Config"/> used to compute from its own,
    /// host-pipeline-bypassing <c>ConfigurationBuilder</c>. Bind() reads from the real host
    /// <see cref="IConfiguration"/> (each process's own <c>builder.Configuration</c>), so this
    /// respects <c>appsettings.{Environment}.json</c> overrides, standard env-var/user-secrets
    /// providers, and is constructor-injectable/mockable via <c>IOptions&lt;AgrumySettings&gt;</c> -
    /// none of which the old CWD-relative static approach could do.
    /// </summary>
    public class AgrumySettings
    {
        public string? DefaultConnection { get; set; }

        /// <summary>Raw value of Database:Provider (mysql | mariadb | postgres | postgresql), env
        /// var AGRUMY_DB_PROVIDER takes precedence. Null/empty =&gt; mysql. Parsed by
        /// api.Dal.DbProviderKindParser.</summary>
        public string? DatabaseProvider { get; set; }

        public string? JwtSecureKey { get; set; }
        public string? JwtIssuer { get; set; }
        public string? JwtAudience { get; set; }

        public string? ApiService { get; set; }

        // ServerConfig:Reload (roadmap #10 hysteresis) - if true, overwrite the DB serverConfig
        // row's hysteresis fields from ServerConfig:Hysteresis on every startup instead of only
        // seeding them once when the row is first created. Defaults false: an operator flips this
        // to force the DB back to the file's values; leaving it true keeps clobbering any admin-UI
        // edit on every restart.
        public bool ServerConfigReload { get; set; }

        // Fallback hysteresis defaults if ServerConfig:Hysteresis is missing from appsettings.json
        // entirely (upgrade from an older config file) - same values the firmware used to hardcode.
        public double HysteresisWaterLevel { get; set; } = 5.0;
        public double HysteresisTemperature { get; set; } = 1.0;
        public double HysteresisHumidity { get; set; } = 5.0;
        public double HysteresisLight { get; set; } = 20.0;

        // Roadmap #28: how long a device's identical repeated event is ignored server-side.
        public int EventDedupeMinutes { get; set; } = 10;

        // Roadmap #24: how long a user must wait between "resend activation email" requests.
        public int ActivationResendCooldownMinutes { get; set; } = 10;

        // Roadmap #64: off by default - UserRegistration rejects an unknown tenant name instead of
        // silently creating one until an admin opts in.
        public bool AllowSelfServiceTenantCreation { get; set; }

        // Roadmap #39: no fallback constant - unlike the numeric defaults above, there is no
        // universally-reasonable default IANA zone to assume for a fleet's physical location. Null
        // (unset) is a valid, common state: TimeZoneHelper.GetUtcOffsetSeconds treats it as UTC
        // (offset 0) rather than throwing, so schedule mode is inert-but-safe until an admin sets
        // this on the Server Settings page.
        public string? ScheduleTimeZone { get; set; }

        // Roadmap #94. LocalPath: where the Local repository keeps its .bin files (relative to the
        // content root unless absolute; null = FirmwareStorage.DefaultRelativePath). GitHubRepository: only the SEED for the DB serverConfig
        // row - the admin page edits the live value. GitHubToken: optional, see HttpFirmwareFetcher.
        public string? FirmwareLocalPath { get; set; }
        public string FirmwareGitHubRepository { get; set; } = "dopiskur/AgrumyDevice";
        public string? FirmwareGitHubToken { get; set; }

        public static AgrumySettings Bind(IConfiguration configuration) => new()
        {
            DefaultConnection = configuration.GetConnectionString("DefaultConnection"),
            DatabaseProvider = Environment.GetEnvironmentVariable("AGRUMY_DB_PROVIDER")
                ?? configuration.GetSection("Database:Provider").Value,
            JwtSecureKey = configuration.GetSection("JWT:SecureKey").Value,
            JwtIssuer = configuration.GetSection("JWT:Issuer").Value,
            JwtAudience = configuration.GetSection("JWT:Audience").Value,
            ApiService = configuration.GetSection("WebView:ApiService").Value,
            ServerConfigReload = bool.TryParse(configuration.GetSection("ServerConfig:Reload").Value, out var reload) && reload,
            HysteresisWaterLevel = ParseDoubleOr(configuration, "ServerConfig:Hysteresis:WaterLevel", 5.0),
            HysteresisTemperature = ParseDoubleOr(configuration, "ServerConfig:Hysteresis:Temperature", 1.0),
            HysteresisHumidity = ParseDoubleOr(configuration, "ServerConfig:Hysteresis:Humidity", 5.0),
            HysteresisLight = ParseDoubleOr(configuration, "ServerConfig:Hysteresis:Light", 20.0),
            EventDedupeMinutes = ParseIntOr(configuration, "ServerConfig:EventDedupeMinutes", 10),
            ActivationResendCooldownMinutes = ParseIntOr(configuration, "ServerConfig:ActivationResendCooldownMinutes", 10),
            AllowSelfServiceTenantCreation = ParseBoolOr(configuration, "ServerConfig:AllowSelfServiceTenantCreation", false),
            ScheduleTimeZone = configuration.GetSection("ServerConfig:ScheduleTimeZone").Value,
            FirmwareLocalPath = configuration.GetSection("Firmware:LocalPath").Value,
            FirmwareGitHubRepository = configuration.GetSection("Firmware:GitHubRepository").Value is { Length: > 0 } repo ? repo : "dopiskur/AgrumyDevice",
            FirmwareGitHubToken = configuration.GetSection("Firmware:GitHubToken").Value,
        };

        // IConfiguration stores every value as its literal JSON text (e.g. "20.0"). Parsing that
        // with the ambient CultureInfo.CurrentCulture is wrong on any host whose locale uses ","
        // as the decimal separator (e.g. hr-HR) - "20.0" would parse as 200 or throw. InvariantCulture
        // makes appsettings.json's "." always mean decimal point, regardless of the OS locale.
        private static double ParseDoubleOr(IConfiguration configuration, string key, double fallback) =>
            double.TryParse(configuration.GetSection(key).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        private static int ParseIntOr(IConfiguration configuration, string key, int fallback) =>
            int.TryParse(configuration.GetSection(key).Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        private static bool ParseBoolOr(IConfiguration configuration, string key, bool fallback) =>
            bool.TryParse(configuration.GetSection(key).Value, out var value) ? value : fallback;
    }
}
