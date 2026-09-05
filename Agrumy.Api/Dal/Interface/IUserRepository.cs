using api.Models;

namespace api.Dal.Interface
{
    /// User facet: accounts, secrets, composable roles, email activation, and the legacy userGroup mapping (groups exist solely as the legacy role assignment).
    public interface IUserRepository
    {
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);

        /// Self-service profile write - only FirstName/LastName/TimeZone, never any authorization-bearing column. False if no such user.
        Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone);

        /// Sole writer of the device-registration PIN - a value+expiry (re)issues it, explicit nulls clear it; not called after a successful registration since the PIN is multi-use within its expiry.
        Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc);

        Task<bool> UserDeleteAsync(int? idUser);

        /// The user matched by id / email / username, or null if none matches (or no key was given).
        Task<User?> UserGetAsync(int? idUser, string? email, string? username);
        Task<IList<User>> UsersGetAsync(int? tenantID);

        /// Every user in every tenant - callers must enforce the global-admin check themselves.
        Task<IList<User>> UsersGetAllAsync();

        /// The password hash+salt for the user matched by id / email / username, or null if none matches.
        Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username);

        Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret);

        /// True while the fresh-install bootstrap Global Admin (see EfRepository.SeedBootstrapAdminAsync) still has PwdHash=NULL - drives Agrumy.Web's first-run "set password" screen, permanently false once claimed.
        Task<bool> BootstrapAdminPendingAsync();

        /// Sets the password on the pending bootstrap admin row (PwdHash IS NULL); false if already claimed or setupSecret doesn't match.
        Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret);

        /// Removes the still-unclaimed bootstrap admin placeholder when ImportAsSentinel claims TenantID=0 with real imported users - the WHERE PwdHash IS NULL guard means it can never touch an already-claimed row. No-op (false) if already claimed or gone.
        Task<bool> BootstrapAdminDiscardPendingAsync();

        Task<IList<UserRole>> UserRoleGetAsync();

        // A user can hold several roles at once - the userUserRole junction table is the sole
        // source of truth for this set.

        /// Every role name currently assigned to this user via userUserRole - empty (never null) for a user nobody has migrated/assigned yet.
        Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser);

        /// Replaces this user's entire role set with exactly <paramref name="roleNames"/> (not incremental) - unknown names are silently ignored since the Web UI only ever offers api.Security.RoleNames.All.
        Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames);

        // Email activation

        /// Attaches a fresh activation token to a just-registered user - always issues, no cooldown check since this is the first send.
        Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt);

        /// Re-issues an activation token for the "resend" flow - returns false (issuing nothing) when already verified or still within cooldownMinutes, so the controller's generic response stays truthful either way.
        Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes);

        /// Marks the user matching this activation token hash as EmailVerified and clears the token - null if the hash matches nothing or already expired.
        Task<User?> UserActivateAsync(string tokenHash);

        /// Every admin-role user in the given tenant, used to notify a tenant's admins - never empty for a real tenant since its creator always becomes its first admin.
        Task<IList<User>> TenantAdminsGetAsync(int tenantId);
    }
}
