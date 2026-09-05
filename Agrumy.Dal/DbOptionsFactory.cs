using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// Builds DbContextOptions for the selected provider - pre-beta, so a fresh database is provisioned via EnsureCreatedAsync() straight from the model, no EF migrations.
    public static class DbOptionsFactory
    {
        public static DbContextOptions<AgrumyDbContext> Build(DbProviderKind provider, string connectionString)
        {
            var builder = new DbContextOptionsBuilder<AgrumyDbContext>();
            switch (provider)
            {
                case DbProviderKind.Postgres:
                    // NpgsqlCompat's module initializer already opted into legacy timestamp behaviour (DateTime -> `timestamp without time zone`).
                    builder.UseNpgsql(connectionString);
                    builder.AddInterceptors(new SessionTimeZoneInterceptor("SET TIME ZONE 'UTC';"));
                    break;
                default:
                    // Fixed MariaDB version keeps construction connection-free (AutoDetect would open a socket during static init).
                    builder.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0)));
                    builder.AddInterceptors(new SessionTimeZoneInterceptor("SET time_zone = '+00:00';"));
                    break;
            }
            return builder.Options;
        }
    }
}
