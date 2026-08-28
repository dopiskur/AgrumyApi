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
        public static string? secureKey = configuration.GetSection("JWT:SecureKey").Value;
        public static string? jwtIssuer = configuration.GetSection("JWT:Issuer").Value;
        public static string? jwtAudience = configuration.GetSection("JWT:Audience").Value;

        public static string? apiService = configuration.GetSection("WebView:ApiService").Value;
    }
}
