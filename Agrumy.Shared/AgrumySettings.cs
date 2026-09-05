using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace api
{
    /// Populated via Bind() from the host's own IConfiguration (env-var/user-secrets overrides included), not a static singleton, so it stays constructor-injectable and mockable.
    public class AgrumySettings
    {
        public string? DefaultConnection { get; set; }

        /// Raw Database:Provider value (mysql|mariadb|postgres|postgresql); AGRUMY_DB_PROVIDER env var takes precedence, null/empty defaults to mysql via api.Dal.DbProviderKindParser.
        public string? DatabaseProvider { get; set; }

        public string? JwtSecureKey { get; set; }
        public string? JwtIssuer { get; set; }
        public string? JwtAudience { get; set; }

        public string? ApiService { get; set; }

        // Overwrites serverConfig's hysteresis fields from config on every startup instead of seeding once; false by default so it doesn't clobber admin-UI edits on restart.
        public bool ServerConfigReload { get; set; }

        // Fallback hysteresis defaults when ServerConfig:Hysteresis is missing entirely - same values firmware used to hardcode.
        public double HysteresisWaterLevel { get; set; } = 5.0;
        public double HysteresisTemperature { get; set; } = 1.0;
        public double HysteresisHumidity { get; set; } = 5.0;
        public double HysteresisLight { get; set; } = 20.0;

        // See api.Models.ServerConfig.BatteryLowThreshold/BatteryLowHysteresis for the dead-zone rule they feed into.
        public double BatteryLowThreshold { get; set; } = 20.0;
        public double BatteryLowHysteresis { get; set; } = 5.0;

        // WaterPump-only safety-limit defaults (30min run/5min cooldown); only seeds new devices, never retroactively changes existing DeviceConfigController rows.
        public int WaterPumpMaxRunSeconds { get; set; } = 1800;
        public int WaterPumpCooldownSeconds { get; set; } = 300;

        // How long a device's identical repeated event is ignored server-side.
        public int EventDedupeMinutes { get; set; } = 10;

        // How long a user must wait between "resend activation email" requests.
        public int ActivationResendCooldownMinutes { get; set; } = 10;

        // Hard ceiling is 32 (AgrumyFirmware DeviceModel.h's MAX_RULES), enforced in ServerConfigApiController.Update - this is only the default.
        public int MaxRulesPerZone { get; set; } = 10;

        // Off by default - UserRegistration rejects unknown tenant names until an admin opts in.
        public bool AllowSelfServiceTenantCreation { get; set; }

        // Off by default - gates the Tenant Management menu item alongside the GlobalAdmin role check.
        public bool TenantManagementEnabled { get; set; }

        // No fallback constant - null is valid and treated as UTC by TimeZoneHelper.GetUtcOffsetSeconds, so schedule mode stays inert-but-safe until an admin sets this.
        public string? ScheduleTimeZone { get; set; }

        // FirmwareLocalPath: relative to content root, null = FirmwareStorage.DefaultRelativePath. FirmwareGitHubRepository only seeds serverConfig - the admin page owns the live value.
        public string? FirmwareLocalPath { get; set; }
        public string FirmwareGitHubRepository { get; set; } = "dopiskur/AgrumyFirmware";
        public string? FirmwareGitHubToken { get; set; }

        // Only seeds serverConfig on first creation - the admin page owns the live value; null/0 disables auto-refresh (manual-only).
        public int? FirmwareRefreshIntervalHours { get; set; } = 24;

        // Days of sensorData history to auto-purge; null = admin hasn't opted in, data just accumulates.
        public int? SensorDataRetentionDays { get; set; }

        // A secret, never exposed through ServerConfigApiController - unlike location/poll-interval/threshold, which are operational and live in api.Models.ServerConfig instead.
        public string? WeatherApiKey { get; set; }
        public int WeatherPollIntervalMinutes { get; set; } = 15;
        public double WeatherRainSkipThreshold { get; set; } = 50.0;

        // Shared with Agrumy.Relay's own RelayOptions.Relay.RegistrationSecret - proves a Register call declaring IsRelay:true actually comes from Agrumy.Relay, not any client holding a valid user email+PIN. Null/empty means no relay may self-register on this server.
        public string? RelayRegistrationSecret { get; set; }

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
            BatteryLowThreshold = ParseDoubleOr(configuration, "ServerConfig:BatteryLowThreshold", 20.0),
            BatteryLowHysteresis = ParseDoubleOr(configuration, "ServerConfig:BatteryLowHysteresis", 5.0),
            WaterPumpMaxRunSeconds = ParseIntOr(configuration, "ServerConfig:WaterPumpMaxRunSeconds", 1800),
            WaterPumpCooldownSeconds = ParseIntOr(configuration, "ServerConfig:WaterPumpCooldownSeconds", 300),
            EventDedupeMinutes = ParseIntOr(configuration, "ServerConfig:EventDedupeMinutes", 10),
            ActivationResendCooldownMinutes = ParseIntOr(configuration, "ServerConfig:ActivationResendCooldownMinutes", 10),
            MaxRulesPerZone = ParseIntOr(configuration, "ServerConfig:MaxRulesPerZone", 10),
            AllowSelfServiceTenantCreation = ParseBoolOr(configuration, "ServerConfig:AllowSelfServiceTenantCreation", false),
            TenantManagementEnabled = ParseBoolOr(configuration, "ServerConfig:TenantManagementEnabled", false),
            ScheduleTimeZone = configuration.GetSection("ServerConfig:ScheduleTimeZone").Value,
            FirmwareLocalPath = configuration.GetSection("Firmware:LocalPath").Value,
            FirmwareGitHubRepository = configuration.GetSection("Firmware:GitHubRepository").Value is { Length: > 0 } repo ? repo : "dopiskur/AgrumyFirmware",
            FirmwareGitHubToken = configuration.GetSection("Firmware:GitHubToken").Value,
            FirmwareRefreshIntervalHours = ParseIntOrNull(configuration, "ServerConfig:FirmwareRefreshIntervalHours") ?? 24,
            SensorDataRetentionDays = ParseIntOrNull(configuration, "ServerConfig:SensorDataRetentionDays"),
            WeatherApiKey = configuration.GetSection("Weather:ApiKey").Value,
            WeatherPollIntervalMinutes = ParseIntOr(configuration, "ServerConfig:WeatherPollIntervalMinutes", 15),
            WeatherRainSkipThreshold = ParseDoubleOr(configuration, "ServerConfig:WeatherRainSkipThreshold", 50.0),
            RelayRegistrationSecret = configuration.GetSection("Relay:RegistrationSecret").Value,
        };

        // CurrentCulture would misparse "20.0" on a comma-decimal locale (e.g. hr-HR); InvariantCulture keeps "." as the decimal point regardless of OS locale.
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

        private static int? ParseIntOrNull(IConfiguration configuration, string key) =>
            int.TryParse(configuration.GetSection(key).Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }
}
