using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace api.Dal
{
    /// <summary>
    /// Lets <c>dotnet ef</c> build an <see cref="AgrumyDbContext"/> without booting the web host.
    /// Connection string resolution order: <c>--connection</c> arg passed to the ef tool, then the
    /// <c>ConnectionStrings__DefaultConnection</c> / <c>DefaultConnection</c> environment variables,
    /// then <c>appsettings.json</c> in the project directory. A real connection is only needed for
    /// commands that touch the database (<c>database update</c>, <c>migrations script --idempotent</c>
    /// against a live server); <c>migrations add</c> uses the pinned server version below.
    /// </summary>
    internal class AgrumyDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AgrumyDbContext>
    {
        public AgrumyDbContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            string? conn =
                GetArg(args, "--connection")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? Environment.GetEnvironmentVariable("DefaultConnection")
                ?? config.GetConnectionString("DefaultConnection")
                ?? "server=localhost;port=3306;database=agrumy;user id=root;password=;";

            var options = new DbContextOptionsBuilder<AgrumyDbContext>()
                .UseMySql(conn, new MariaDbServerVersion(new Version(11, 4, 0)))
                .Options;

            return new AgrumyDbContext(options);
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
