using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IUserRepository + ITenantRepository members (roadmap #74 split).</summary>
    internal partial class EfRepository
    {
        public async Task UserAddAsync(User user, UserSecret userSecret)
        {
            await using var db = Db();
            db.Users.Add(new UserRow
            {
                TenantID = user.TenantID ?? 0,
                Email = user.Email ?? "",
                Username = user.Username,
                DevicePin = user.DevicePin,
                DevicePinExpires = user.DevicePinExpires,
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
            // Roadmap #70: DevicePin deliberately NOT written here - the PIN lifecycle (generate/
            // expire/consume) lives exclusively in UserSetDevicePinAsync below, so an admin edit
            // can never resurrect a consumed PIN or hand-craft a weak one.
            row.Username = user.Username;
            row.FirstName = user.FirstName;
            row.LastName = user.LastName;
            row.Phone = user.Phone;
            row.UserGroupID = user.UserGroupID;
            row.Enabled = user.Enabled;
            row.TimeZone = user.TimeZone;
            await db.SaveChangesAsync();
        }

        /// <summary>Self-service profile write (roadmap #71 follow-up): deliberately touches ONLY the
        /// three profile columns so the endpoint can never alter Enabled/UserGroupID/TenantID even if
        /// the controller mis-binds - the column list here IS the authorization boundary.</summary>
        public async Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (row == null)
            {
                return false;
            }

            row.FirstName = firstName;
            row.LastName = lastName;
            row.TimeZone = timeZone;
            await db.SaveChangesAsync();
            return true;
        }

        /// <summary>Roadmap #70: the ONLY writer of DevicePin/DevicePinExpires - pass nulls to
        /// consume a PIN after a successful device registration, a value+expiry to (re)issue one.</summary>
        public async Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc)
        {
            await using var db = Db();
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row == null)
            {
                return false;
            }

            row.DevicePin = devicePin;
            row.DevicePinExpires = expiresAtUtc;
            await db.SaveChangesAsync();
            return true;
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

        // UserGet / UsersGet joined only userGroup (not userRole), so RoleName stays null and
        // UserRoleID comes from userGroup.
        private static User ToDto(UserRow u, UserGroupRow g) => new()
        {
            IDUser = u.IDUser,
            TenantID = u.TenantID,
            Email = u.Email,
            Username = u.Username,
            DevicePin = u.DevicePin,
            DevicePinExpires = u.DevicePinExpires,
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
            TimeZone = u.TimeZone,
        };
    }
}
