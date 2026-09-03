namespace api.Dal
{
    /// <summary>
    /// Which relational provider the app talks to. Selected by <c>Database:Provider</c> in
    /// appsettings (<c>mysql</c> | <c>mariadb</c> | <c>postgres</c> | <c>postgresql</c>), the
    /// <c>AGRUMY_DB_PROVIDER</c> environment variable, or a <c>--provider</c> arg to the ef tool.
    /// </summary>
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
