using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IUserRepository core members: user CRUD/lookup/password reset and
    /// TenantAdminsGetAsync here; bootstrap admin in EfRepository.Users.Bootstrap.cs, composable
    /// roles in EfRepository.Users.Roles.cs, email activation in EfRepository.Users.Activation.cs,
    /// tenant CRUD (ITenantRepository) in EfRepository.Tenants.cs, and user groups in
    /// EfRepository.Users.Groups.cs.</summary>
    internal partial class EfRepository
    {
        public async Task UserAddAsync(User user, UserSecret userSecret)
        {
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
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == user.IDUser);
            if (row == null)
            {
                return;
            }

            row.TenantID = user.TenantID ?? 0;
            row.Email = user.Email ?? "";
            // DevicePin deliberately NOT written here - the PIN lifecycle (generate/expire) lives
            // exclusively in UserSetDevicePinAsync below, so an admin edit can never resurrect an
            // expired PIN or hand-craft a weak one.
            row.Username = user.Username;
            row.FirstName = user.FirstName;
            row.LastName = user.LastName;
            row.Phone = user.Phone;
            row.UserGroupID = user.UserGroupID;
            row.Enabled = user.Enabled;
            row.TimeZone = user.TimeZone;
            await db.SaveChangesAsync();
        }

        /// <summary>Self-service profile write: deliberately touches ONLY the three profile columns
        /// so the endpoint can never alter Enabled/UserGroupID/TenantID even if the controller
        /// mis-binds - the column list here IS the authorization boundary.</summary>
        public async Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone)
        {
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

        /// <summary>The ONLY writer of DevicePin/DevicePinExpires - a value+expiry to (re)issue a
        /// PIN, nulls to explicitly clear one. A successful device registration does NOT call this
        /// (the PIN is multi-use within its 24h window, not consumed on first use).</summary>
        public async Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc)
        {
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
            // Protects the default admin/user (id 1). Callers already enforce this, but keep it here too.
            if (idUser is null or <= 1)
            {
                return false;
            }

            int rows = await db.Users.Where(u => u.IDUser == idUser).ExecuteDeleteAsync();
            return rows > 0;
        }

        public async Task<User?> UserGetAsync(int? idUser, string? email, string? username)
        {
            // LEFT JOIN: a user with no group (bootstrap admin, or any UserGroupID left NULL) must
            // still be found by lookup - an INNER JOIN here made every login 401 on a fresh install,
            // since a brand new userGroup table has no rows to match against.
            var q = from u in db.Users.AsNoTracking()
                    join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup into gj
                    from g in gj.DefaultIfEmpty()
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
            // See UserGetAsync above for why this is a LEFT JOIN, not an INNER JOIN.
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup into gj
                              from g in gj.DefaultIfEmpty()
                              where u.TenantID == tenantID
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        // Same query as UsersGetAsync minus the tenant filter - callers (UserApiController) only
        // reach this after confirming the caller is a TenantID==0 admin.
        public async Task<IList<User>> UsersGetAllAsync()
        {
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup into gj
                              from g in gj.DefaultIfEmpty()
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        public async Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username)
        {
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
            int rows = await db.Users.Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, userSecret.PwdHash ?? "")
                    .SetProperty(u => u.PwdSalt, userSecret.PwdSalt ?? ""));
            return rows > 0;
        }

        // A tenant can never have zero admins - its creator becomes one at registration - so this
        // is never empty for a real tenant.
        public async Task<IList<User>> TenantAdminsGetAsync(int tenantId)
        {
            // Same LEFT JOIN as UserGetAsync/UsersGetAsync (a groupless user must still resolve
            // instead of vanishing from the query), chained into a second LEFT JOIN for the role -
            // the "r != null" in the where clause below is what actually excludes groupless/roleless
            // users here, not the join type, so this reads identically to the old INNER JOIN pair.
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup into gj
                              from g in gj.DefaultIfEmpty()
                              join r in db.UserRoles.AsNoTracking() on g!.UserRoleID equals r.IDUserRole into rj
                              from r in rj.DefaultIfEmpty()
                              where u.TenantID == tenantId && r != null && r.RoleName == "admin"
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
        }

        private static User ToDto(UserRow u, UserGroupRow? g) => new()
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
            UserRoleID = g?.UserRoleID,
            GroupName = g?.GroupName,
            Enabled = u.Enabled,
            DateCreated = u.DateCreated,
            DateModified = u.DateModified,
            EmailVerified = u.EmailVerified,
            TimeZone = u.TimeZone,
        };
    }
}
