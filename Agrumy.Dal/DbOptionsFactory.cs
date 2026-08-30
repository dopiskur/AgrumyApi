using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{AgrumyDbContext}"/> for the selected provider.
    /// Pre-beta: no EF migrations - a fresh database is provisioned with <c>EnsureCreatedAsync()</c>
    /// straight from the model (roadmap #42).
    /// </summary>
    public static class DbOptionsFactory
    {
        public static DbContextOptions<AgrumyDbContext> Build(DbProviderKind provider, string connectionString)
        {
            var builder = new DbContextOptionsBuilder<AgrumyDbContext>();
            switch (provider)
            {
                case DbProviderKind.Postgres:
                    // NpgsqlCompat's module initializer has already opted into legacy timestamp
                    // behaviour (DateTime -> `timestamp without time zone`, any DateTimeKind).
                    builder.UseNpgsql(connectionString);
                    break;
                default:
                    // Fixed MariaDB version keeps construction connection-free (AutoDetect would
                    // open a socket during static init).
                    builder.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0)));
                    break;
            }
            return builder.Options;
        }
    }
}
