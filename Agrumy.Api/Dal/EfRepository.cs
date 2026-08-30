using System.Globalization;
using System.Text.Json.Nodes;
using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
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
    /// </summary>
    internal class EfRepository : IRepository
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

        // ---- Server config ------------------------------------------------------------

        public async Task<ServerConfig> ServerConfigGetAsync(int idServerConfig = 1)
        {
            await using var db = Db();
            var row = await db.ServerConfigs.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);

            if (row != null)
            {
                return ToDto(row);
            }

            // No row: generate a default one (mirrors the old ServerConfigGetAsync + ServerConfigAddAsync),
            // seeding the hysteresis fields from appsettings.json so a fresh install has sane
            // defaults before an admin ever visits the settings page.
            var generated = new ServerConfigRow
            {
                IDServerConfig = idServerConfig,
                ServerConfigName = "DefaultGenerated" + idServerConfig,
                ConfigKey = Guid.NewGuid().ToString(),
                PortHTTP = 80,
                PortHTTPS = 443,
                WaterLevelHysteresis = Config.hysteresisWaterLevel,
                TemperatureHysteresis = Config.hysteresisTemperature,
                HumidityHysteresis = Config.hysteresisHumidity,
                LightHysteresis = Config.hysteresisLight,
                EventDedupeMinutes = Config.eventDedupeMinutes,
                ActivationResendCooldownMinutes = Config.activationResendCooldownMinutes,
                AllowSelfServiceTenantCreation = Config.allowSelfServiceTenantCreation,
            };
            db.ServerConfigs.Add(generated);
            await db.SaveChangesAsync();
            return ToDto(generated);
        }

        public async Task ServerConfigUpdateAsync(ServerConfig config)
        {
            await using var db = Db();
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == config.IDServerConfig);
            if (row == null)
            {
                return;
            }

            row.WaterLevelHysteresis = config.WaterLevelHysteresis;
            row.TemperatureHysteresis = config.TemperatureHysteresis;
            row.HumidityHysteresis = config.HumidityHysteresis;
            row.LightHysteresis = config.LightHysteresis;
            row.EventDedupeMinutes = config.EventDedupeMinutes;
            row.ActivationResendCooldownMinutes = config.ActivationResendCooldownMinutes;
            row.AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation;
            await db.SaveChangesAsync();
        }

        /// <summary>Forces the DB serverConfig row's hysteresis fields back to appsettings.json's
        /// ServerConfig:Hysteresis values, creating the row if it does not exist yet. Only called
        /// at startup when ServerConfig:Reload is true - see Config.serverConfigReload.</summary>
        public async Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig = 1)
        {
            await using var db = Db();
            var row = await db.ServerConfigs.FirstOrDefaultAsync(s => s.IDServerConfig == idServerConfig);
            if (row == null)
            {
                row = new ServerConfigRow
                {
                    IDServerConfig = idServerConfig,
                    ServerConfigName = "DefaultGenerated" + idServerConfig,
                    ConfigKey = Guid.NewGuid().ToString(),
                    PortHTTP = 80,
                    PortHTTPS = 443,
                };
                db.ServerConfigs.Add(row);
            }

            row.WaterLevelHysteresis = Config.hysteresisWaterLevel;
            row.TemperatureHysteresis = Config.hysteresisTemperature;
            row.HumidityHysteresis = Config.hysteresisHumidity;
            row.LightHysteresis = Config.hysteresisLight;
            row.EventDedupeMinutes = Config.eventDedupeMinutes;
            row.ActivationResendCooldownMinutes = Config.activationResendCooldownMinutes;
            row.AllowSelfServiceTenantCreation = Config.allowSelfServiceTenantCreation;
            await db.SaveChangesAsync();
        }

        // ---- User -------------------------------------------------------------------

        public async Task UserAddAsync(User user, UserSecret userSecret)
        {
            await using var db = Db();
            db.Users.Add(new UserRow
            {
                TenantID = user.TenantID ?? 0,
                Email = user.Email ?? "",
                Username = user.Username,
                DevicePin = user.DevicePin,
                PwdHash = userSecret.PwdHash ?? "",
                PwdSalt = userSecret.PwdSalt ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.Phone,
                UserGroupID = user.UserGroupID,
                Enabled = user.Enabled,
                EmailVerified = user.EmailVerified ?? false,
            });
            await db.SaveChangesAsync();
        }

        public async Task UserUpdateAsync(User user)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == user.IDUser);
            if (row == null)
            {
                return; // proc UPDATE ... WHERE IDUser = ? simply affects no rows
            }

            row.TenantID = user.TenantID ?? 0;
            row.Email = user.Email ?? "";
            row.DevicePin = user.DevicePin;
            row.Username = user.Username;
            row.FirstName = user.FirstName;
            row.LastName = user.LastName;
            row.Phone = user.Phone;
            row.UserGroupID = user.UserGroupID;
            row.Enabled = user.Enabled;
            await db.SaveChangesAsync();
        }

        public async Task<bool> UserDeleteAsync(int? idUser)
        {
            // Proc guard: IF (idUser > 1) - protects the default admin/user. Callers already
            // enforce this, but keep it here too.
            if (idUser is null or <= 1)
            {
                return false;
            }

            await using var db = Db();
            int rows = await db.Users.Where(u => u.IDUser == idUser).ExecuteDeleteAsync();
            return rows > 0;
        }

        public async Task<User?> UserGetAsync(int? idUser, string? email, string? username)
        {
            await using var db = Db();

            // Inner join to userGroup, exactly as the UserGet proc.
            var q = from u in db.Users.AsNoTracking()
                    join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
                    select new { u, g };

            if (idUser != null)
            {
                q = q.Where(x => x.u.IDUser == idUser);
            }
            else if (idUser == null && email != null && username == null)
            {
                q = q.Where(x => x.u.Email == email);
            }
            else if (idUser == null && email == null && username != null)
            {
                q = q.Where(x => x.u.Username == username);
            }
            else
            {
                throw new ArgumentException("Provide an id, email, or username to look a user up by.");
            }

            var hit = await q.FirstOrDefaultAsync();
            return hit == null ? null : ToDto(hit.u, hit.g);
        }

        public async Task<IList<User>> UsersGetAsync(int? tenantID)
        {
            await using var db = Db();
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
                              where u.TenantID == tenantID
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        // Roadmap #65: same query as UsersGetAsync minus the tenant filter - callers (UserApiController)
        // only reach this after confirming the caller is a TenantID==0 admin.
        public async Task<IList<User>> UsersGetAllAsync()
        {
            await using var db = Db();
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        public async Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username)
        {
            await using var db = Db();
            IQueryable<UserRow> q = db.Users.AsNoTracking();

            if (idUser != null)
            {
                q = q.Where(u => u.IDUser == idUser);
            }
            else if (idUser == null && email != null && username == null)
            {
                q = q.Where(u => u.Email == email);
            }
            else if (idUser == null && email == null && username != null)
            {
                q = q.Where(u => u.Username == username);
            }
            else
            {
                throw new ArgumentException("Provide an id, email, or username to look a secret up by.");
            }

            return await q.Select(u => new UserSecret { PwdHash = u.PwdHash, PwdSalt = u.PwdSalt })
                          .FirstOrDefaultAsync();
        }

        public async Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret)
        {
            await using var db = Db();
            int rows = await db.Users.Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, userSecret.PwdHash ?? "")
                    .SetProperty(u => u.PwdSalt, userSecret.PwdSalt ?? ""));
            return rows > 0;
        }

        public async Task<IList<UserRole>> UserRoleGetAsync()
        {
            await using var db = Db();
            return await db.UserRoles.AsNoTracking()
                .Select(r => new UserRole { IDUserRole = r.IDUserRole, RoleName = r.RoleName, RoleScopeID = r.RoleScopeID })
                .ToListAsync();
        }

        // ---- Composable roles (roadmap #66) ------------------------------------------

        public async Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser)
        {
            await using var db = Db();
            return await (from ur in db.UserUserRoles.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on ur.UserRoleID equals r.IDUserRole
                          where ur.UserID == idUser && r.RoleName != null
                          select r.RoleName!).ToListAsync();
        }

        public async Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames)
        {
            await using var db = Db();
            var wanted = roleNames.ToHashSet();

            var roleIds = await db.UserRoles.AsNoTracking()
                .Where(r => r.RoleName != null && wanted.Contains(r.RoleName))
                .Select(r => r.IDUserRole)
                .ToListAsync();

            var existing = await db.UserUserRoles.Where(x => x.UserID == idUser).ToListAsync();
            db.UserUserRoles.RemoveRange(existing.Where(x => !roleIds.Contains(x.UserRoleID)));
            db.UserUserRoles.AddRange(roleIds
                .Where(id => existing.All(x => x.UserRoleID != id))
                .Select(id => new UserUserRoleRow { UserID = idUser, UserRoleID = id }));

            await db.SaveChangesAsync();
        }

        // ---- Email activation (roadmap #24) -----------------------------------------

        public async Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row is null) { return; }

            row.ActivationTokenHash = tokenHash;
            row.ActivationTokenExpiresAt = expiresAt;
            row.ActivationLastSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row is null || row.EmailVerified)
            {
                return false;
            }
            if (row.ActivationLastSentAt is DateTime lastSent && lastSent > DateTime.UtcNow.AddMinutes(-cooldownMinutes))
            {
                return false; // still in cooldown
            }

            row.ActivationTokenHash = tokenHash;
            row.ActivationTokenExpiresAt = expiresAt;
            row.ActivationLastSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<User?> UserActivateAsync(string tokenHash)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.ActivationTokenHash == tokenHash);
            if (row is null || row.ActivationTokenExpiresAt is null || row.ActivationTokenExpiresAt < DateTime.UtcNow)
            {
                return null;
            }

            row.EmailVerified = true;
            row.ActivationTokenHash = null;
            row.ActivationTokenExpiresAt = null;
            await db.SaveChangesAsync();

            UserGroupRow? group = await db.UserGroups.AsNoTracking().FirstOrDefaultAsync(g => g.IDUserGroup == row.UserGroupID);
            return group is null ? null : ToDto(row, group);
        }

        // Roadmap #63: a tenant can never have zero admins - its creator becomes one at registration
        // (see UserApiController.UserRegistration) - so this is never empty for a real tenant.
        public async Task<IList<User>> TenantAdminsGetAsync(int tenantId)
        {
            await using var db = Db();
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
                              join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                              where u.TenantID == tenantId && r.RoleName == "admin"
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        // ---- Refresh tokens ---------------------------------------------------------

        public async Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt)
        {
            await using var db = Db();
            var row = new RefreshTokenRow
            {
                UserID = userID,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
            };
            db.RefreshTokens.Add(row);
            await db.SaveChangesAsync();
            return row.IDRefreshToken;
        }

        public async Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash)
        {
            await using var db = Db();
            var row = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            return row == null
                ? null
                : new RefreshTokenInfo { UserID = row.UserID, ExpiresAt = row.ExpiresAt, RevokedAt = row.RevokedAt };
        }

        public async Task RefreshTokenRotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt)
        {
            await using var db = Db();
            var old = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == oldTokenHash);
            if (old == null || old.RevokedAt != null)
            {
                return; // caller already checked expiry/reuse; nothing valid left to rotate
            }

            old.RevokedAt = DateTime.UtcNow;
            old.ReplacedByTokenHash = newTokenHash;
            db.RefreshTokens.Add(new RefreshTokenRow
            {
                UserID = old.UserID,
                TokenHash = newTokenHash,
                ExpiresAt = newExpiresAt,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(); // one transaction: revoke old + insert new
        }

        public async Task RefreshTokenRevokeAsync(string tokenHash)
        {
            await using var db = Db();
            await db.RefreshTokens.Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

        public async Task RefreshTokenRevokeAllForUserAsync(int userID)
        {
            await using var db = Db();
            await db.RefreshTokens.Where(t => t.UserID == userID && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

        // ---- Device ---------------------------------------------------------------

        public async Task DeviceAddAsync(Device device)
        {
            // Outside the transaction below: read-only, and ServerConfigGetAsync opens its own
            // connection via Db() - auto-generates the row on a brand-new install.
            ServerConfig serverConfig = await ServerConfigGetAsync();

            await using var db = Db();
            await using var tx = await db.Database.BeginTransactionAsync();

            var sensorCfg = new DeviceConfigSensorRow();
            var controllerCfg = new DeviceConfigControllerRow
            {
                // Hysteresis starts at the server-wide default; admin can override per device
                // afterwards under Device -> Controller.
                WaterLevelHysteresis = serverConfig.WaterLevelHysteresis,
                TemperatureHysteresis = serverConfig.TemperatureHysteresis,
                HumidityHysteresis = serverConfig.HumidityHysteresis,
                LightHysteresis = serverConfig.LightHysteresis,
            };
            db.DeviceConfigSensors.Add(sensorCfg);
            db.DeviceConfigControllers.Add(controllerCfg);
            await db.SaveChangesAsync();

            db.Devices.Add(new DeviceRow
            {
                TenantID = device.TenantID,
                DeviceTypeID = device.DeviceTypeID,
                DeviceUnitID = device.DeviceUnitID,
                DeviceUnitZoneID = device.DeviceUnitZoneID,
                DeviceName = device.DeviceName,
                MacAddress = device.MacAddress,
                ApiId = device.ApiId ?? "",
                ApiKey = device.ApiKey ?? "",
                ServicePoint = device.ServicePoint,
                DeviceTypeServiceID = device.DeviceTypeServiceID,
                DeviceSensorEnabled = device.DeviceSensorEnabled,
                DeviceConfigSensorID = sensorCfg.IDDeviceConfigSensor,
                DeviceControllerEnabled = device.DeviceControllerEnabled,
                DeviceConfigControllerID = controllerCfg.IDDeviceConfigController,
                BatteryEnabled = device.BatteryEnabled,
                Enabled = device.Enabled,
                ConfigVersion = device.ConfigVersion,
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task DeviceDeleteAsync(int? idDevice, int? tenantID)
        {
            await using var db = Db();
            var target = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice && d.TenantID == tenantID)
                .Select(d => new { d.DeviceConfigSensorID, d.DeviceConfigControllerID })
                .FirstOrDefaultAsync();
            if (target == null)
            {
                return;
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Devices.Where(d => d.IDDevice == idDevice && d.TenantID == tenantID).ExecuteDeleteAsync();
            if (target.DeviceConfigSensorID != null)
            {
                await db.DeviceConfigSensors.Where(c => c.IDDeviceConfigSensor == target.DeviceConfigSensorID).ExecuteDeleteAsync();
            }
            if (target.DeviceConfigControllerID != null)
            {
                await db.DeviceConfigControllers.Where(c => c.IDDeviceConfigController == target.DeviceConfigControllerID).ExecuteDeleteAsync();
            }
            await tx.CommitAsync();
        }

        public async Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress)
        {
            await using var db = Db();
            IQueryable<DeviceRow> q = db.Devices.AsNoTracking().Where(d => d.TenantID == tenantID);

            if (idDevice != null)
            {
                q = q.Where(d => d.IDDevice == idDevice);
            }
            else if (idDevice == null && apiId != null && macAddress == null)
            {
                q = q.Where(d => d.ApiId == apiId);
            }
            else if (idDevice == null && apiId == null && macAddress != null)
            {
                q = q.Where(d => d.MacAddress == macAddress);
            }
            else
            {
                return null; // no lookup key
            }

            var row = await q.FirstOrDefaultAsync();
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByIdAsync(int? idDevice)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByApiIdAsync(string? apiId)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiId == apiId);
            return row == null ? null : ToDto(row);
        }

        public async Task<IList<Device>> DevicesGetAsync(int? tenantID)
        {
            await using var db = Db();
            var rows = await db.Devices.AsNoTracking().Where(d => d.TenantID == tenantID).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // #66 Phase 2: same query minus the tenant filter - callers (DeviceApiController) only
        // reach this after CallerReadsDevicesGlobally passed, mirroring UsersGetAllAsync.
        public async Task<IList<Device>> DevicesGetAllAsync()
        {
            await using var db = Db();
            var rows = await db.Devices.AsNoTracking().ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress)
        {
            await using var db = Db();
            return await db.Devices.AsNoTracking()
                .AnyAsync(d => d.TenantID == tenantID && d.MacAddress == macAddress);
        }

        public async Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID)
        {
            await using var db = Db();
            var row = await db.DeviceConfigSensors.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID)
        {
            await using var db = Db();
            var row = await db.DeviceConfigControllers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigSensorID == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID)
        {
            await using var db = Db();
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigControllerID == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID)
        {
            await using var db = Db();
            return await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.DeviceTypeID == deviceTypeID)
                .OrderByDescending(f => f.DateAdded)
                .Select(f => new DeviceFirmware
                {
                    IDDeviceFirmware = f.IDDeviceFirmware,
                    DeviceTypeID = f.DeviceTypeID,
                    Version = f.Version,
                    Url = f.Url,
                    DateAdded = f.DateAdded,
                })
                .FirstOrDefaultAsync();
        }

        public async Task DeviceUpdateAsync(Device? device)
        {
            if (device == null)
            {
                return;
            }

            await using var db = Db();
            var row = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == device.IDDevice);
            if (row == null)
            {
                return;
            }

            // Columns the DeviceUpdate proc touched (note: it did NOT set DeviceUnitZoneID,
            // MacAddress or the config-id columns).
            row.TenantID = device.TenantID;
            row.DeviceTypeID = device.DeviceTypeID;
            row.DeviceTypeServiceID = device.DeviceTypeServiceID;
            row.DeviceUnitID = device.DeviceUnitID;
            row.DeviceName = device.DeviceName;
            row.ApiId = device.ApiId ?? "";
            row.ApiKey = device.ApiKey ?? "";
            row.ServicePoint = device.ServicePoint;
            row.ServicePublicKey = device.ServicePublicKey;
            row.SleepSeconds = device.SleepSeconds;
            row.SleepDeepEnabled = device.SleepDeepEnabled;
            row.DeviceSensorEnabled = device.DeviceSensorEnabled;
            row.DeviceControllerEnabled = device.DeviceControllerEnabled;
            row.BatteryEnabled = device.BatteryEnabled;
            row.Enabled = device.Enabled;
            row.Debug = device.Debug;
            row.ConfigVersion = (device.ConfigVersion ?? 0) + 1; // proc: ConfigVersion = configVersion + 1
            await db.SaveChangesAsync();
        }

        public async Task DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            await using var db = Db();

            var row = await db.DeviceConfigControllers
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == cfg.IDDeviceConfigController);
            if (row != null)
            {
                // The proc declared these params as int (columns are double) so historically the
                // values were truncated. Phase 1 stores the real double instead - a deliberate,
                // documented deviation from the proc.
                row.TempLow = cfg.TempLow;
                row.TempHigh = cfg.TempHigh;
                row.HumidLow = cfg.HumidLow;
                row.HumidHigh = cfg.HumidHigh;
                row.MoistLow = cfg.MoistLow;
                row.MoistHigh = cfg.MoistHigh;
                row.LightLow = cfg.LightLow;
                row.LightHigh = cfg.LightHigh;
                row.WaterLow = cfg.WaterLow;
                row.WaterHigh = cfg.WaterHigh;
                row.WaterLevelHysteresis = cfg.WaterLevelHysteresis;
                row.TemperatureHysteresis = cfg.TemperatureHysteresis;
                row.HumidityHysteresis = cfg.HumidityHysteresis;
                row.LightHysteresis = cfg.LightHysteresis;
                row.VentilationIntervalEnabled = cfg.VentilationIntervalEnabled;
                row.VentilationInterval = cfg.VentilationInterval;
                row.VentilationIntervalLenght = cfg.VentilationIntervalLenght;
                row.LightIntervalEnabled = cfg.LightIntervalEnabled;
                row.LightInterval = cfg.LightInterval;
                row.LightIntervalLenght = cfg.LightIntervalLenght;
                row.HeatingIntervalEnabled = cfg.HeatingIntervalEnabled;
                row.HeatingInterval = cfg.HeatingInterval;
                row.HeatingIntervalLenght = cfg.HeatingIntervalLenght;
                row.WaterPumpIntervalEnabled = cfg.WaterPumpIntervalEnabled;
                row.WaterPumpInterval = cfg.WaterPumpInterval;
                row.WaterPumpIntervalLenght = cfg.WaterPumpIntervalLenght;
                row.RelayEnabled = cfg.RelayEnabled;
                row.Relay1 = cfg.Relay1;
                row.Relay2 = cfg.Relay2;
                row.Relay3 = cfg.Relay3;
                row.Relay4 = cfg.Relay4;
                row.Relay5 = cfg.Relay5;
                row.Relay6 = cfg.Relay6;
                row.Relay7 = cfg.Relay7;
                row.Relay8 = cfg.Relay8;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        public async Task DeviceConfigSensorUpdateAsync(int? idDevice, DeviceConfigSensor? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            await using var db = Db();

            var row = await db.DeviceConfigSensors
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == cfg.IDDeviceConfigSensor);
            if (row != null)
            {
                row.SensorBattery = cfg.SensorBattery;
                row.SensorTemp = cfg.SensorTemp;
                row.SensorTempSoil = cfg.SensorTempSoil;
                row.SensorHumid = cfg.SensorHumid;
                row.SensorMoist = cfg.SensorMoist;
                row.SensorLight = cfg.SensorLight;
                row.SensorCo2 = cfg.SensorCo2;
                row.SensorTvoc = cfg.SensorTvoc;
                row.SensorBarometer = cfg.SensorBarometer;
                row.SensorPH = cfg.SensorPH;
                row.SensorRainLevel = cfg.SensorRainLevel;
                row.SensorWaterLevel = cfg.SensorWaterLevel;
                row.SensorWind = cfg.SensorWind;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        public async Task<IList<DeviceType>> DeviceTypeGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypes.AsNoTracking()
                .Select(t => new DeviceType
                {
                    IDDeviceType = t.IDDeviceType,
                    DeviceTypeName = t.DeviceTypeName,
                    SensorEnabled = t.SensorEnabled,
                    ControllerEnabled = t.ControllerEnabled,
                })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypeServices.AsNoTracking()
                .Select(s => new DeviceTypeService { IDDeviceTypeService = s.IDDeviceTypeService, ServiceType = s.ServiceType })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypeRelays.AsNoTracking()
                .Select(r => new DeviceTypeRelay { IDDeviceTypeRelay = r.IDDeviceTypeRelay, RelayName = r.RelayName })
                .ToListAsync();
        }

        public async Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync()
        {
            await using var db = Db();
            return await db.DeviceTypeSensors.AsNoTracking()
                .Select(s => new DeviceTypeSensor
                {
                    IDDeviceTypeSensor = s.IDDeviceTypeSensor,
                    SensorName = s.SensorName,
                    SensorDescription = s.SensorDescription,
                    Battery = s.Battery,
                    Temperature = s.Temperature,
                    TemperatureSoil = s.TemperatureSoil,
                    Humidity = s.Humidity,
                    Moisture = s.Moisture,
                    Light = s.Light,
                    Co2 = s.Co2,
                    Tvoc = s.Tvoc,
                    Barometer = s.Barometer,
                    WaterPH = s.WaterPH,
                    WaterTankLevel = s.WaterTankLevel,
                    RainLevel = s.RainLevel,
                    Wind = s.Wind,
                })
                .ToListAsync();
        }

        // ---- SensorData ---------------------------------------------------------

        public async Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID)
        {
            var rows = new List<SensorDataRow>();
            foreach (var node in jsonArray)
            {
                if (node is not JsonObject o)
                {
                    continue;
                }

                DateTime? dc = ReadDateTime(o, "dateCreated");
                rows.Add(new SensorDataRow
                {
                    // Identity is server-authoritative: it comes from the authenticated device, not
                    // the payload. The deviceID/tenantID/deviceUnitID/deviceUnitZoneID keys in each
                    // JSON row are deliberately ignored.
                    DeviceID = deviceID,
                    TenantID = tenantID,
                    DeviceUnitID = deviceUnitID ?? 0,
                    DeviceUnitZoneID = deviceUnitZoneID ?? 0,
                    Battery = ReadInt(o, "battery"),
                    Temperature = ReadDouble(o, "temperature"),
                    SoilTemperature = ReadDouble(o, "soilTemperature"),
                    Humidity = ReadDouble(o, "humidity"),
                    Moisture = ReadInt(o, "moisture"),
                    Light = ReadInt(o, "light"),
                    Co2 = ReadInt(o, "co2"),
                    Tvoc = ReadInt(o, "tvoc"),
                    Barometer = ReadDouble(o, "barometer"),
                    LiquidPH = ReadDouble(o, "liquidPH"),
                    RainLevel = ReadInt(o, "rainLevel"),
                    WaterLevel = ReadInt(o, "waterLevel"),
                    Wind = ReadInt(o, "wind"),
                    // Replaces the sensorData_SetDateTimeOnNull trigger: a missing/blank timestamp
                    // becomes "now".
                    DateCreated = dc ?? DateTime.Now,
                });
            }

            if (rows.Count == 0)
            {
                return;
            }

            await using var db = Db();
            db.SensorData.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public async Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return ""; // proc: ELSE branch / NULL interval -> SQL NULL -> read as ""
            }

            DateTime now = DateTime.Now;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            await using var db = Db();
            var rows = await db.SensorData.AsNoTracking()
                .Where(r => r.DeviceID == deviceID
                            && r.TenantID == tenantID
                            && r.Co2 != null && r.Co2 < 8000   // matches SensorDataReportBuilder: NULL Co2 rows are excluded
                            && r.DateCreated > cutoff)
                .ToListAsync();

            string json = SensorReportShaper.Build(rows, timeMDMY.Value);

            if (json.Length > 0 && buildReport > 0)
            {
                // The proc hard-coded deviceID 1000038 here - a bug: every saved report was
                // attributed to one device. Save it against the device the report is actually for.
                db.SensorDataReports.Add(new SensorDataReportRow
                {
                    DeviceID = deviceID,
                    ReportName = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SensorData = json,
                });
                await db.SaveChangesAsync();
            }

            return json;
        }

        public async Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? reportID)
        {
            await using var db = Db();

            if (getData == 0)
            {
                return await (from r in db.SensorDataReports.AsNoTracking()
                              join d in db.Devices.AsNoTracking() on r.DeviceID equals d.IDDevice
                              where r.DeviceID == deviceID && d.TenantID == tenantID
                              select new SensorDataReport
                              {
                                  IDSensorDataReport = r.IDSensorDataReport,
                                  DeviceID = r.DeviceID,
                                  ReportName = r.ReportName,
                                  DateGenerated = r.DateGenerated,
                              }).ToListAsync();
            }

            if (getData > 0)
            {
                return await (from r in db.SensorDataReports.AsNoTracking()
                              join d in db.Devices.AsNoTracking() on r.DeviceID equals d.IDDevice
                              where r.IDSensorDataReport == reportID && d.TenantID == tenantID
                              select new SensorDataReport
                              {
                                  IDSensorDataReport = r.IDSensorDataReport,
                                  DeviceID = r.DeviceID,
                                  ReportName = r.ReportName,
                                  DateGenerated = r.DateGenerated,
                                  SensorData = r.SensorData,
                              }).ToListAsync();
            }

            return new List<SensorDataReport>(); // proc CASE has no matching WHEN and no ELSE
        }

        public async Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return; // proc CASE has no ELSE
            }

            DateTime now = DateTime.Now;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            await using var db = Db();
            await db.SensorData
                .Where(r => r.DeviceID == deviceID && r.TenantID == tenantID && r.DateCreated < cutoff)
                .ExecuteDeleteAsync();
        }

        // ---- Device events (roadmap #28) -------------------------------------------

        public async Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message)
        {
            // Read outside the write connection, same reasoning as DeviceAddAsync's ServerConfigGetAsync
            // call - auto-generates the row (and its EventDedupeMinutes default) on a brand-new install.
            int dedupeMinutes = (await ServerConfigGetAsync()).EventDedupeMinutes ?? Config.eventDedupeMinutes;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-dedupeMinutes);

            await using var db = Db();

            bool isDuplicate = await db.EventDevices.AsNoTracking()
                .AnyAsync(e => e.DeviceID == deviceID && e.EventID == (int)eventType && e.Date >= cutoff);
            if (isDuplicate)
            {
                return false;
            }

            db.EventDevices.Add(new EventDeviceRow
            {
                DeviceID = deviceID,
                TenantID = tenantID,
                EventID = (int)eventType,
                Date = DateTime.UtcNow, // server clock, not device-reported - a device mid-"NoInternet" may lack NTP sync
                Message = message,
            });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100)
        {
            await using var db = Db();
            var rows = await db.EventDevices.AsNoTracking()
                .Where(e => e.DeviceID == deviceID && e.TenantID == tenantID)
                .OrderByDescending(e => e.Date)
                .Take(limit)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        private static DeviceEvent ToDto(EventDeviceRow e) => new()
        {
            IDEventDevice = e.IDEventDevice,
            DeviceID = e.DeviceID,
            // Guards against a row written by a future/older enum definition than this build's -
            // never throws, just surfaces the raw number so it's still visible in the admin list.
            EventType = Enum.IsDefined(typeof(DeviceEventType), e.EventID)
                ? ((DeviceEventType)e.EventID).ToString()
                : $"Unknown({e.EventID})",
            Message = e.Message,
            CreatedAt = e.Date,
        };

        // ---- Tenant ---------------------------------------------------------

        public async Task<bool> TenantGetAsync(string tenantName)
        {
            await using var db = Db();
            return await db.Tenants.AsNoTracking().AnyAsync(t => t.TenantName == tenantName);
        }

        public async Task<int?> TenantGetIdAsync(string tenantName)
        {
            await using var db = Db();
            return await db.Tenants.AsNoTracking()
                .Where(t => t.TenantName == tenantName)
                .Select(t => (int?)t.IDTenant)
                .FirstOrDefaultAsync();
        }

        public async Task<int> TenantAddAsync(string tenantName)
        {
            await using var db = Db();
            var row = new TenantRow { TenantName = tenantName };
            db.Tenants.Add(row);
            await db.SaveChangesAsync();
            return row.IDTenant;
        }

        // ---- Group ---------------------------------------------------------

        public async Task<IList<UserGroup>> UserGroupsGetAsync()
        {
            await using var db = Db();
            return await (from g in db.UserGroups.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                          select new UserGroup
                          {
                              IDUserGroup = g.IDUserGroup,
                              GroupName = g.GroupName,
                              UserRoleID = g.UserRoleID,
                              RoleName = r.RoleName,
                          }).ToListAsync();
        }

        public async Task<UserGroup?> UserGroupGetAsync(int? idUserGroup)
        {
            await using var db = Db();
            return await (from g in db.UserGroups.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                          where g.IDUserGroup == idUserGroup
                          select new UserGroup
                          {
                              IDUserGroup = g.IDUserGroup,
                              GroupName = g.GroupName,
                              UserRoleID = g.UserRoleID,
                              RoleName = r.RoleName,
                          }).FirstOrDefaultAsync();
        }

        public async Task UserGroupDeleteAsync(int? idUserGroup)
        {
            if (idUserGroup is null or <= 0)
            {
                return; // proc guard: IF (idUserGroup > 0)
            }
            await using var db = Db();
            await db.UserGroups.Where(g => g.IDUserGroup == idUserGroup).ExecuteDeleteAsync();
        }

        public async Task UserGroupAddAsync(UserGroup userGroup)
        {
            await using var db = Db();
            db.UserGroups.Add(new UserGroupRow
            {
                GroupName = userGroup.GroupName,
                UserRoleID = userGroup.UserRoleID,
            });
            await db.SaveChangesAsync();
        }

        // ---- Projections --------------------------------------------------------

        private static ServerConfig ToDto(ServerConfigRow r) => new()
        {
            IDServerConfig = r.IDServerConfig,
            ServerConfigName = r.ServerConfigName,
            ConfigKey = r.ConfigKey,
            PortHTTP = r.PortHTTP,
            PortHTTPS = r.PortHTTPS,
            WaterLevelHysteresis = r.WaterLevelHysteresis,
            TemperatureHysteresis = r.TemperatureHysteresis,
            HumidityHysteresis = r.HumidityHysteresis,
            LightHysteresis = r.LightHysteresis,
            EventDedupeMinutes = r.EventDedupeMinutes,
            ActivationResendCooldownMinutes = r.ActivationResendCooldownMinutes,
            AllowSelfServiceTenantCreation = r.AllowSelfServiceTenantCreation,
        };

        // UserGet / UsersGet joined only userGroup (not userRole), so RoleName stays null and
        // UserRoleID comes from userGroup.
        private static User ToDto(UserRow u, UserGroupRow g) => new()
        {
            IDUser = u.IDUser,
            TenantID = u.TenantID,
            Email = u.Email,
            Username = u.Username,
            DevicePin = u.DevicePin,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            UserGroupID = u.UserGroupID,
            UserRoleID = g.UserRoleID,
            GroupName = g.GroupName,
            Enabled = u.Enabled,
            DateCreated = u.DateCreated,
            DateModified = u.DateModified,
            EmailVerified = u.EmailVerified,
        };

        private static Device ToDto(DeviceRow d) => new()
        {
            IDDevice = d.IDDevice,
            TenantID = d.TenantID,
            DeviceTypeID = d.DeviceTypeID,
            DeviceUnitID = d.DeviceUnitID,
            DeviceUnitZoneID = d.DeviceUnitZoneID,
            DeviceConfigSensorID = d.DeviceConfigSensorID,
            DeviceConfigControllerID = d.DeviceConfigControllerID,
            DeviceTypeServiceID = d.DeviceTypeServiceID,
            DeviceName = d.DeviceName,
            MacAddress = d.MacAddress,
            ApiId = d.ApiId,
            ApiKey = d.ApiKey,
            ServicePoint = d.ServicePoint,
            ServicePublicKey = d.ServicePublicKey,
            SleepSeconds = d.SleepSeconds,
            SleepDeepEnabled = d.SleepDeepEnabled,
            DeviceSensorEnabled = d.DeviceSensorEnabled,
            DeviceControllerEnabled = d.DeviceControllerEnabled,
            BatteryEnabled = d.BatteryEnabled,
            Debug = d.Debug,
            Reboot = d.Reboot,
            Reset = d.Reset,
            FirmwareUpdate = d.FirmwareUpdate,
            Enabled = d.Enabled,
            ConfigVersion = d.ConfigVersion,
            DateCreated = d.DateCreated,
            DateModified = d.DateModified,
        };

        private static DeviceConfigSensor ToDto(DeviceConfigSensorRow c) => new()
        {
            IDDeviceConfigSensor = c.IDDeviceConfigSensor,
            SensorBattery = c.SensorBattery,
            SensorTemp = c.SensorTemp,
            SensorTempSoil = c.SensorTempSoil,
            SensorHumid = c.SensorHumid,
            SensorMoist = c.SensorMoist,
            SensorLight = c.SensorLight,
            SensorCo2 = c.SensorCo2,
            SensorTvoc = c.SensorTvoc,
            SensorBarometer = c.SensorBarometer,
            SensorPH = c.SensorPH,
            SensorRainLevel = c.SensorRainLevel,
            SensorWaterLevel = c.SensorWaterLevel,
            SensorWind = c.SensorWind,
        };

        private static DeviceConfigController ToDto(DeviceConfigControllerRow c) => new()
        {
            IDDeviceConfigController = c.IDDeviceConfigController,
            TempLow = c.TempLow,
            TempHigh = c.TempHigh,
            HumidLow = c.HumidLow,
            HumidHigh = c.HumidHigh,
            MoistLow = c.MoistLow,
            MoistHigh = c.MoistHigh,
            LightLow = c.LightLow,
            LightHigh = c.LightHigh,
            WaterLow = c.WaterLow,
            WaterHigh = c.WaterHigh,
            WaterLevelHysteresis = c.WaterLevelHysteresis,
            TemperatureHysteresis = c.TemperatureHysteresis,
            HumidityHysteresis = c.HumidityHysteresis,
            LightHysteresis = c.LightHysteresis,
            VentilationIntervalEnabled = c.VentilationIntervalEnabled,
            VentilationInterval = c.VentilationInterval,
            VentilationIntervalLenght = c.VentilationIntervalLenght,
            LightIntervalEnabled = c.LightIntervalEnabled,
            LightInterval = c.LightInterval,
            LightIntervalLenght = c.LightIntervalLenght,
            HeatingIntervalEnabled = c.HeatingIntervalEnabled,
            HeatingInterval = c.HeatingInterval,
            HeatingIntervalLenght = c.HeatingIntervalLenght,
            WaterPumpIntervalEnabled = c.WaterPumpIntervalEnabled,
            WaterPumpInterval = c.WaterPumpInterval,
            WaterPumpIntervalLenght = c.WaterPumpIntervalLenght,
            RelayEnabled = c.RelayEnabled,
            Relay1 = c.Relay1,
            Relay2 = c.Relay2,
            Relay3 = c.Relay3,
            Relay4 = c.Relay4,
            Relay5 = c.Relay5,
            Relay6 = c.Relay6,
            Relay7 = c.Relay7,
            Relay8 = c.Relay8,
        };

        // ---- JSON value coercion (firmware sends measurements as strings or null) --------

        private static int? ReadInt(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)l;
            if (v.TryGetValue(out double d)) return (int)d;
            if (v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var si)) return si;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sd)) return (int)sd;
            }
            return null;
        }

        private static double? ReadDouble(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)
                && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sd)) return sd;
            return null;
        }

        private static DateTime? ReadDateTime(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out DateTime dt)) return dt;
            if (v.TryGetValue(out string? s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd))
            {
                return sd;
            }
            return null;
        }
    }
}
