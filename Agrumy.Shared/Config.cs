using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace api
{
    public class Config
    {
        private static IConfigurationRoot configuration = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json")
           .Build();

        // SQL settings
        public static string? defaultSqlCon = configuration.GetConnectionString("DefaultConnection");

        /// <summary>Raw value of <c>Database:Provider</c> (mysql | mariadb | postgres | postgresql). Null/empty =&gt; mysql. Parsed by api.Dal.DbProviderKindParser.</summary>
        public static string? dbProvider =
            Environment.GetEnvironmentVariable("AGRUMY_DB_PROVIDER")
            ?? configuration.GetSection("Database:Provider").Value;
        public static string? secureKey = configuration.GetSection("JWT:SecureKey").Value;
        public static string? jwtIssuer = configuration.GetSection("JWT:Issuer").Value;
        public static string? jwtAudience = configuration.GetSection("JWT:Audience").Value;

        public static string? apiService = configuration.GetSection("WebView:ApiService").Value;

        // ServerConfig:Reload (roadmap #10 hysteresis) - if true, overwrite the DB serverConfig
        // row's hysteresis fields from ServerConfig:Hysteresis on every startup instead of only
        // seeding them once when the row is first created. An operator flips this to force the
        // DB back to the file's values; leaving it true keeps clobbering any admin-UI edit on
        // every restart, so it defaults to false.
        public static bool serverConfigReload =
            bool.TryParse(configuration.GetSection("ServerConfig:Reload").Value, out var reload) && reload;

        // Fallback hysteresis defaults if ServerConfig:Hysteresis is missing from appsettings.json
        // entirely (upgrade from an older config file) - same values the firmware used to hardcode.
        public static double hysteresisWaterLevel = ParseDoubleOr("ServerConfig:Hysteresis:WaterLevel", 5.0);
        public static double hysteresisTemperature = ParseDoubleOr("ServerConfig:Hysteresis:Temperature", 1.0);
        public static double hysteresisHumidity = ParseDoubleOr("ServerConfig:Hysteresis:Humidity", 5.0);
        public static double hysteresisLight = ParseDoubleOr("ServerConfig:Hysteresis:Light", 20.0);

        // IConfiguration stores every value as its literal JSON text (e.g. "20.0"). Parsing that
        // with the ambient CultureInfo.CurrentCulture is wrong on any host whose locale uses "."
        // as a group separator (e.g. hr-HR) - "20.0" silently becomes 200. InvariantCulture makes
        // appsettings.json's "." always mean decimal point, regardless of the OS locale.
        private static double ParseDoubleOr(string key, double fallback) =>
            double.TryParse(configuration.GetSection(key).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }
}
