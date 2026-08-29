using Microsoft.EntityFrameworkCore.Design;

namespace api.Dal
{
    /// <summary>
    /// Lets <c>dotnet ef</c> build an <see cref="AgrumyDbContext"/> without booting the web host.
    ///
    /// Provider (roadmap #42 Phase 2): <c>--provider mysql|postgres</c> ef arg, else
    /// <c>AGRUMY_DB_PROVIDER</c> env var, else mysql.
    /// Connection: <c>--connection</c> ef arg, else <c>ConnectionStrings__DefaultConnection</c> /
    /// <c>DefaultConnection</c> env vars, else a localhost placeholder. Only commands that touch the
    /// database need a real connection; <c>migrations add</c> uses the fixed provider versions.
    ///
    /// Add a migration:
    ///   MySQL:    dotnet ef migrations add NAME -p Agrumy.Api.Migrations.MySql    -s Agrumy.Api -- --provider mysql
    ///   Postgres: dotnet ef migrations add NAME -p Agrumy.Api.Migrations.Postgres -s Agrumy.Api -- --provider postgres
    /// </summary>
    public class AgrumyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AgrumyDbContext>
    {
        public AgrumyDbContext CreateDbContext(string[] args)
        {
            var provider = DbProviderKindParser.Parse(
                GetArg(args, "--provider")
                ?? Environment.GetEnvironmentVariable("AGRUMY_DB_PROVIDER"));

            string conn =
                GetArg(args, "--connection")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? Environment.GetEnvironmentVariable("DefaultConnection")
                ?? (provider == DbProviderKind.Postgres
                        ? "Host=localhost;Port=5432;Database=agrumyapi;Username=postgres;Password=postgres"
                        : "server=localhost;port=3306;database=agrumyapi;user id=root;password=;");

            return new AgrumyDbContext(DbOptionsFactory.Build(provider, conn));
        }

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
