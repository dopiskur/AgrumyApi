using api.Dal.Entities;
using api.Dal.Interface;
using api.Security;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace api.Dal
{
    /// <summary>
    /// EF Core implementation of <see cref="IRepository"/>. Replaces the Dapper +
    /// <c>CommandType.StoredProcedure</c> <c>SqlRepository</c> (roadmap #42).
    ///
    /// Runs on MySQL/MariaDB (Pomelo) or PostgreSQL (Npgsql), chosen by <c>Database:Provider</c>.
    /// Every method reproduces the effect of the stored procedure it replaced; the reference for
    /// that behaviour is the pre-#42 <c>Schema/SchemaScripts.cs</c> (git history). Persistence goes
    /// through <see cref="Entities"/> row types; results are projected onto the <see cref="api.Models"/>
    /// DTOs so the interface contract is unchanged.
    ///
    /// Roadmap #74: one class, split into partial files mirroring the IRepository facets
    /// (EfRepository.Users.cs, EfRepository.Devices.cs, ...) - this file holds the connection
    /// plumbing and the ISystemRepository members.
    /// </summary>
    internal partial class EfRepository : IRepository
    {
        /// <summary>
        /// Test-only seam: point the repository at an integration database before the first call.
        /// Mirrors <c>RepoFactory.OverrideForTests</c>. Null =&gt; use appsettings.
        /// </summary>
        internal static string? ConnectionStringOverride { get; set; }

        /// <summary>Test-only seam: force the provider. Null =&gt; use <c>Database:Provider</c>.</summary>
        internal static DbProviderKind? ProviderOverride { get; set; }

        // Built once for the normal (appsettings-driven) path. Not cached when a test seam is set,
        // so an integration test can point successive calls at different engines.
        private static DbContextOptions<AgrumyDbContext>? _options;

        private static AgrumyDbContext Db()
        {
            if (ConnectionStringOverride != null || ProviderOverride != null)
            {
                return new AgrumyDbContext(DbOptionsFactory.Build(
                    ProviderOverride ?? DbProviderKindParser.Parse(Config.dbProvider),
                    ConnectionStringOverride
                        ?? Config.defaultSqlCon
                        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.")));
            }

            _options ??= DbOptionsFactory.Build(
                DbProviderKindParser.Parse(Config.dbProvider),
                Config.defaultSqlCon
                    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing."));
            return new AgrumyDbContext(_options);
        }

        // ---- Startup / health -----------------------------------------------------------

        public async Task<bool> TestConnectionAsync()
        {
            await using var db = Db();
            return await db.Database.CanConnectAsync();
        }

        public async Task EnsureSchemaAsync()
        {
            await using var db = Db();

            // Pre-beta: no real data to preserve across schema changes, so we skip migration
            // history entirely and just create-if-missing from the current model. Empty DB gets
            // every table from AgrumyDbContext as it stands today; shared DB with tables already
            // present is a no-op either way (EnsureCreatedAsync also no-ops if the DB isn't empty).
            // Migrations come back at beta - see roadmap.
            await db.Database.EnsureCreatedAsync();

            // #66: EnsureCreatedAsync makes tables, never rows - without the role catalog a fresh
            // install would have every login fall back to the legacy single-role path forever and
            // registration's UserRolesSetAsync would silently assign nothing. Insert-if-missing by
            // name; on a DB migrated via 2026-08-30-rbac-composable-roles.sql these already exist
            // (with proper RoleScopeIDs, which nothing reads for authorization) so this is a no-op.
            var existingNames = await db.UserRoles.AsNoTracking()
                .Where(r => r.RoleName != null).Select(r => r.RoleName!).ToListAsync();
            var missing = RoleNames.All.Except(existingNames).ToList();
            if (missing.Count > 0)
            {
                db.UserRoles.AddRange(missing.Select(name => new UserRoleRow { RoleName = name }));
                await db.SaveChangesAsync();
            }
        }

        public DbFailureKind ClassifyException(Exception ex)
        {
            Exception inner = ex is DbUpdateException due && due.InnerException != null ? due.InnerException : ex;

            // MySql error numbers: 1146 ER_NO_SUCH_TABLE, 1051 ER_BAD_TABLE_ERROR, 1305 SP_DOES_NOT_EXIST;
            // 1216/1217/1451/1452 FK violation, 1062 duplicate key, 1213 deadlock, 1205 lock-wait timeout.
            if (inner is MySqlException mysqlEx)
            {
                switch (mysqlEx.Number)
                {
                    case 1146:
                    case 1051:
                    case 1305:
                        return DbFailureKind.SchemaMissing;
                    case 1216:
                    case 1217:
                    case 1451:
                    case 1452:
                    case 1062:
                        return DbFailureKind.ConstraintViolation;
                    case 1213:
                    case 1205:
                        return DbFailureKind.Contention;
                }
                // Any other MySql error still reached the server but failed - treat as a connection-level failure.
                return DbFailureKind.ConnectionFailure;
            }

            // PostgreSQL SQLSTATE: 42P01 undefined_table, 42703 undefined_column, 3F000 invalid_schema_name;
            // 23503 FK violation, 23505 unique violation, 23514 check violation; 40P01 deadlock,
            // 40001 serialization failure, 55P03 lock not available.
            if (inner is PostgresException pgEx)
            {
                switch (pgEx.SqlState)
                {
                    case "42P01":
                    case "42703":
                    case "3F000":
                        return DbFailureKind.SchemaMissing;
                    case "23503":
                    case "23505":
                    case "23514":
                        return DbFailureKind.ConstraintViolation;
                    case "40P01":
                    case "40001":
                    case "55P03":
                        return DbFailureKind.Contention;
                }
                return DbFailureKind.ConnectionFailure;
            }

            // MySQL text fallback for a missing table when the exception type isn't MySqlException.
            // (PostgreSQL's "relation ... does not exist" is covered by the 42P01 SqlState above -
            // a bare "does not exist" match would also swallow unrelated errors like a missing
            // trigger definer.)
            if (DbErrorResponse.Mentions(ex, "doesn't exist") ||
                DbErrorResponse.Mentions(ex, "Unknown table"))
            {
                return DbFailureKind.SchemaMissing;
            }

            // Genuine transport-level failures still mean "can't reach the DB, retry later" (503).
            if (ex is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException ||
                inner is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException)
            {
                return DbFailureKind.ConnectionFailure;
            }

            // Anything else escaping an action (e.g. a not-found ArgumentException from the DAL) is a
            // server-side bug, not a database outage - let it surface as 500, not a misleading 503.
            return DbFailureKind.Unknown;
        }
    }
}
