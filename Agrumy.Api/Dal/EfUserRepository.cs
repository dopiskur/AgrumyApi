using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IUserRepository, extracted out of the EfRepository god class (roadmap #246) - accounts, secrets, composable roles, email activation, and bootstrap admin. RegisterUserAsync needs ITenantRepository (silent tenant-create on registration) and RevokeUserTokensAsync needs IRefreshTokenRepository, both already-extracted leaf facets.
    internal sealed class EfUserRepository(AgrumyDbContext db, ITenantRepository tenantRepository, IRefreshTokenRepository refreshTokenRepository) : IUserRepository
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
                Enabled = user.Enabled,
                EmailVerified = user.EmailVerified ?? false,
                MustChangePassword = user.MustChangePassword,
            });
            await db.SaveChangesAsync();
        }

        public async Task<int> RegisterUserAsync(User user, UserSecret userSecret, int? existingTenantId, string? newTenantName,
            string activationTokenHash, DateTime activationTokenExpiresAtUtc, IEnumerable<string> startingRoles)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            user.TenantID = existingTenantId ?? await tenantRepository.TenantAddAsync(newTenantName!);
            await UserAddAsync(user, userSecret);

            // UserAddAsync doesn't return the new IDUser - re-fetch by the just-inserted unique email.
            User added = await UserGetAsync(null, user.Email, null)
                ?? throw new InvalidOperationException("UserAddAsync did not persist the expected row.");
            await UserSetActivationTokenAsync(added.IDUser!.Value, activationTokenHash, activationTokenExpiresAtUtc);
            await UserRolesSetAsync(added.IDUser.Value, startingRoles);

            await transaction.CommitAsync();
            return added.IDUser.Value;
        }

        public async Task UserUpdateAsync(User user)
        {
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == user.IDUser);
            if (row == null)
            {
                return;
            }

            row.TenantID = user.TenantID ?? 0;
            string newEmail = user.Email ?? "";
            // Changing the address invalidates verification of the OLD one - without this an unconfirmed new address inherits the old address's verified status.
            if (!string.Equals(row.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                row.EmailVerified = false;
            }
            row.Email = newEmail;
            // DevicePin deliberately NOT written here - the PIN lifecycle lives exclusively in UserSetDevicePinAsync, so an admin edit can never resurrect an expired PIN or hand-craft a weak one.
            row.Username = user.Username;
            row.FirstName = user.FirstName;
            row.LastName = user.LastName;
            row.Phone = user.Phone;
            row.Enabled = user.Enabled;
            row.TimeZone = user.TimeZone;
            await db.SaveChangesAsync();
        }

        /// Self-service profile write - deliberately touches only the three profile columns, so the endpoint can't alter Enabled/TenantID even if the controller mis-binds.
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

        /// The only writer of DevicePin/DevicePinExpires - a successful device registration does NOT call this, since the PIN is multi-use within its 24h window, not consumed on first use.
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

            // userRefreshToken's FK is NoAction (RevokeAllForUserAsync only sets RevokedAt, never deletes rows) - without this, deleting a user who has ever logged in throws a FK violation instead of succeeding. userUserRole's FK is Cascade, so it needs no explicit cleanup here.
            await db.RefreshTokens.Where(t => t.UserID == idUser).ExecuteDeleteAsync();

            int rows = await db.Users.Where(u => u.IDUser == idUser).ExecuteDeleteAsync();
            return rows > 0;
        }

        public async Task<User?> UserGetAsync(int? idUser, string? email, string? username)
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
                throw new ArgumentException("Provide an id, email, or username to look a user up by.");
            }

            var hit = await q.FirstOrDefaultAsync();
            return hit == null ? null : ToDto(hit);
        }

        public async Task<IList<User>> UsersGetAsync(int? tenantID)
        {
            var rows = await db.Users.AsNoTracking().Where(u => u.TenantID == tenantID).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        // Same query as UsersGetAsync minus the tenant filter - callers (UserApiController) only reach this after confirming the caller is a TenantID==0 admin.
        public async Task<IList<User>> UsersGetAllAsync()
        {
            var rows = await db.Users.AsNoTracking().ToListAsync();
            return rows.Select(ToDto).ToList();
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
            var row = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (row == null)
            {
                return false;
            }

            row.PwdHash = userSecret.PwdHash ?? "";
            row.PwdSalt = userSecret.PwdSalt ?? "";
            // Any successful password change satisfies "you changed your password" - clearing the flag here avoids duplicating the write in every caller.
            row.MustChangePassword = false;
            await db.SaveChangesAsync();
            await RevokeUserTokensAsync(row.IDUser);
            return true;
        }

        public async Task RevokeUserTokensAsync(int idUser)
        {
            await db.Users.Where(u => u.IDUser == idUser)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokensValidAfterUtc, DateTime.UtcNow));
            await refreshTokenRepository.RefreshTokenRevokeAllForUserAsync(idUser);
        }

        // Never empty for a real tenant since its creator becomes an admin at registration - TenantID 0 has no owning admin, so Global admin is the equivalent role there.
        public async Task<IList<User>> TenantAdminsGetAsync(int tenantId)
        {
            string adminRoleName = tenantId == 0 ? RoleNames.GlobalAdmin : RoleNames.TenantAdmin;
            var rows = await (from u in db.Users.AsNoTracking()
                              join uur in db.UserUserRoles.AsNoTracking() on u.IDUser equals uur.UserID
                              join r in db.UserRoles.AsNoTracking() on uur.UserRoleID equals r.IDUserRole
                              where u.TenantID == tenantId && r.RoleName == adminRoleName
                              select u).Distinct().ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<bool> BootstrapAdminPendingAsync()
        {
            return await db.Users.AsNoTracking().AnyAsync(u => u.PwdHash == null);
        }

        /// Fetch-then-verify-then-write (verification needs C#), but still race-safe since the final write stays gated by WHERE PwdHash IS NULL.
        public async Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret)
        {
            UserRow? pending = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PwdHash == null);
            if (pending == null || !AuthenticationProvider.VerifyHash(pending.BootstrapSecretHash, pending.BootstrapSecretSalt, setupSecret))
            {
                return false;
            }

            // WHERE PwdHash IS NULL (not a Login/email match) is what makes the door close permanently once used - clearing BootstrapSecret* isn't load-bearing for replay but leaves no live secret hash lying around.
            int rows = await db.Users.Where(u => u.PwdHash == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, secret.PwdHash)
                    .SetProperty(u => u.PwdSalt, secret.PwdSalt)
                    .SetProperty(u => u.BootstrapSecretHash, (string?)null)
                    .SetProperty(u => u.BootstrapSecretSalt, (string?)null));
            return rows > 0;
        }

        public async Task<bool> BootstrapAdminDiscardPendingAsync()
        {
            int rows = await db.Users.Where(u => u.PwdHash == null).ExecuteDeleteAsync();
            return rows > 0;
        }

        public async Task<IList<UserRole>> UserRoleGetAsync()
        {
            return await db.UserRoles.AsNoTracking()
                .Select(r => new UserRole { IDUserRole = r.IDUserRole, RoleName = r.RoleName, RoleScopeID = r.RoleScopeID })
                .ToListAsync();
        }

        public async Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser)
        {
            return await (from ur in db.UserUserRoles.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on ur.UserRoleID equals r.IDUserRole
                          where ur.UserID == idUser && r.RoleName != null
                          select r.RoleName!).ToListAsync();
        }

        public async Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames)
        {
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

        public async Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt)
        {
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row is null) { return; }

            row.ActivationTokenHash = tokenHash;
            row.ActivationTokenExpiresAt = expiresAt;
            row.ActivationLastSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes)
        {
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
            var row = await db.Users.FirstOrDefaultAsync(u => u.ActivationTokenHash == tokenHash);
            if (row is null || row.ActivationTokenExpiresAt is null || row.ActivationTokenExpiresAt < DateTime.UtcNow)
            {
                return null;
            }

            row.EmailVerified = true;
            row.ActivationTokenHash = null;
            row.ActivationTokenExpiresAt = null;
            await db.SaveChangesAsync();

            return ToDto(row);
        }

        private static User ToDto(UserRow u) => new()
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
            Enabled = u.Enabled,
            DateCreated = u.DateCreated,
            DateModified = u.DateModified,
            EmailVerified = u.EmailVerified,
            TimeZone = u.TimeZone,
            MustChangePassword = u.MustChangePassword,
            TokensValidAfterUtc = u.TokensValidAfterUtc,
        };
    }
}
