using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IUserRepository core members (roadmap #74 split, further split by roadmap #113):
    /// user CRUD/lookup/password reset and TenantAdminsGetAsync here; bootstrap admin (roadmap #91)
    /// in EfRepository.Users.Bootstrap.cs, composable roles (roadmap #66) in
    /// EfRepository.Users.Roles.cs, email activation (roadmap #24) in
    /// EfRepository.Users.Activation.cs, tenant CRUD (ITenantRepository) in
    /// EfRepository.Tenants.cs, and user groups in EfRepository.Users.Groups.cs.</summary>
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
                return; // proc UPDATE ... WHERE IDUser = ? simply affects no rows
            }

            row.TenantID = user.TenantID ?? 0;
            row.Email = user.Email ?? "";
            // Roadmap #70: DevicePin deliberately NOT written here - the PIN lifecycle (generate/
            // expire) lives exclusively in UserSetDevicePinAsync below, so an admin edit can never
            // resurrect an expired PIN or hand-craft a weak one.
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

        /// <summary>Roadmap #70: the ONLY writer of DevicePin/DevicePinExpires - a value+expiry to
        /// (re)issue a PIN, nulls to explicitly clear one. A successful device registration does
        /// NOT call this (the PIN is multi-use within its 24h window, not consumed on first use -
        /// see the follow-up note on DeviceApiController.DeviceRegistration).</summary>
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
            // Proc guard: IF (idUser > 1) - protects the default admin/user. Callers already
            // enforce this, but keep it here too.
            if (idUser is null or <= 1)
            {
                return false;
            }

            int rows = await db.Users.Where(u => u.IDUser == idUser).ExecuteDeleteAsync();
            return rows > 0;
        }

        public async Task<User?> UserGetAsync(int? idUser, string? email, string? username)
        {

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
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
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

        // Roadmap #63: a tenant can never have zero admins - its creator becomes one at registration
        // (see UserApiController.UserRegistration) - so this is never empty for a real tenant.
        public async Task<IList<User>> TenantAdminsGetAsync(int tenantId)
        {
            var rows = await (from u in db.Users.AsNoTracking()
                              join g in db.UserGroups.AsNoTracking() on u.UserGroupID equals g.IDUserGroup
                              join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                              where u.TenantID == tenantId && r.RoleName == "admin"
                              select new { u, g }).ToListAsync();
            return rows.Select(x => ToDto(x.u, x.g)).ToList();
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
