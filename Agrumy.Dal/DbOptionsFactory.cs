using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{AgrumyDbContext}"/> for the selected provider
    /// (roadmap #42 Phase 2). Each provider points EF at its own migrations project so the two
    /// baseline migrations never collide.
    /// </summary>
    public static class DbOptionsFactory
    {
        public const string MySqlMigrationsAssembly = "Agrumy.Api.Migrations.MySql";
        public const string PostgresMigrationsAssembly = "Agrumy.Api.Migrations.Postgres";

        public static DbContextOptions<AgrumyDbContext> Build(DbProviderKind provider, string connectionString)
        {
            var builder = new DbContextOptionsBuilder<AgrumyDbContext>();
            switch (provider)
            {
                case DbProviderKind.Postgres:
                    // NpgsqlCompat's module initializer has already opted into legacy timestamp
                    // behaviour (DateTime -> `timestamp without time zone`, any DateTimeKind).
                    builder.UseNpgsql(connectionString,
                        o => o.MigrationsAssembly(PostgresMigrationsAssembly));
                    break;
                default:
                    // Fixed MariaDB version keeps construction connection-free (AutoDetect would
                    // open a socket during static init).
                    builder.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0)),
                        o => o.MigrationsAssembly(MySqlMigrationsAssembly));
                    break;
            }
            return builder.Options;
        }
    }
}
