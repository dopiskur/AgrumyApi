namespace api.Dal
{
    /// Which relational provider the app talks to - selected by Database:Provider in appsettings, the AGRUMY_DB_PROVIDER env var, or a --provider arg to the ef tool.
    public enum DbProviderKind
    {
        MySql,
        Postgres,
    }

    public static class DbProviderKindParser
    {
        public static DbProviderKind Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" or "pg" => DbProviderKind.Postgres,
            "" or "mysql" or "mariadb" or "pomelo" => DbProviderKind.MySql,
            var other => throw new InvalidOperationException(
                $"Unknown Database:Provider '{other}'. Use 'mysql'/'mariadb' or 'postgres'/'postgresql'."),
        };
    }
}
