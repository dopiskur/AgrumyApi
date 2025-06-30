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

        public static string? apiService = configuration.GetSection("WebView:ApiService").Value;




        // Koristim konstante
        // public static bool? tenantEnabled = bool.TryParse(configuration.GetSection("Tenant:Enabled").Value, out bool result);
        //public static string? defaultTenantID = configuration.GetSection("DefaultUser:TenantID").Value;
        //public static string? defaultRoleID = configuration.GetSection("DefaultUser:RoleID").Value;
        //public static string? defaultEnabled = configuration.GetSection("DefaultUser:Enabled").Value;
    }
}
