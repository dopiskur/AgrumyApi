using api.Models;

namespace api.Dal.Interface
{
    /// <summary>User facet of the data layer: accounts, secrets, composable roles, email
    /// activation, and the legacy userGroup mapping (kept here because groups exist solely as the
    /// legacy role assignment for users).</summary>
    public interface IUserRepository
    {
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);

        /// <summary>Self-service profile write - only FirstName/LastName/TimeZone, never any
        /// authorization-bearing column (see EfRepository.UserProfileSetAsync). False if no such user.</summary>
        Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone);

        /// <summary>Sole writer of the device-registration PIN - a value+expiry (re)issues it,
        /// nulls explicitly clear it (NOT called after a successful registration; the PIN is
        /// multi-use within its own expiry). False if no such user.</summary>
        Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc);

        Task<bool> UserDeleteAsync(int? idUser);

        /// <summary>The user matched by id / email / username, or null if none matches (or no key was given).</summary>
        Task<User?> UserGetAsync(int? idUser, string? email, string? username);
        Task<IList<User>> UsersGetAsync(int? tenantID);

        /// <summary>Every user in every tenant - callers must enforce the global-admin check themselves.</summary>
        Task<IList<User>> UsersGetAllAsync();

        /// <summary>The password hash+salt for the user matched by id / email / username, or null if none matches.</summary>
        Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username);

        Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret);

        /// <summary>True while the fresh-install bootstrap Global Admin (seeded by
        /// EfRepository.SeedBootstrapAdminAsync with PwdHash=NULL) still has no password - drives
        /// Agrumy.Web's first-run "set password" screen. Always false once
        /// BootstrapAdminSetPasswordAsync has succeeded once, since nothing else ever leaves
        /// PwdHash NULL again.</summary>
        Task<bool> BootstrapAdminPendingAsync();

        /// <summary>Sets the password on the pending bootstrap admin row (PwdHash IS NULL); false if already claimed or setupSecret doesn't match.</summary>
        Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret);

        /// <summary>Removes the still-unclaimed bootstrap admin placeholder as part
        /// of ImportAsSentinel claiming TenantID=0 with real imported users instead - the WHERE
        /// PwdHash IS NULL guard means this can never touch a row someone has actually claimed,
        /// unlike UserDeleteAsync's blanket "id &lt;= 1" protection which exists for the opposite
        /// reason (stop an admin from deleting a REAL account 1). No-op (false) if already claimed
        /// or already gone.</summary>
        Task<bool> BootstrapAdminDiscardPendingAsync();

        Task<IList<UserRole>> UserRoleGetAsync();

        // A user can hold several roles at once - the userUserRole junction table is the sole
        // source of truth for this set.

        /// <summary>Every role name currently assigned to this user via userUserRole. Empty (never
        /// null) for a user nobody has migrated/assigned yet.</summary>
        Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser);

        /// <summary>Replaces this user's ENTIRE role set with exactly <paramref name="roleNames"/> -
        /// not incremental. Unknown role names are silently ignored (defensive - the Web UI only
        /// ever offers api.Security.RoleNames.All as choices).</summary>
        Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames);

        // Email activation

        /// <summary>Attaches a fresh activation token to a just-registered user. Always issues - no
        /// cooldown check, this is the first send.</summary>
        Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt);

        /// <summary>Re-issues an activation token for the "resend" flow. Returns false (issuing
        /// nothing) when the user is already verified or the last send is still within
        /// cooldownMinutes, so the controller's generic "if that account exists" response stays
        /// truthful either way without a separate state check.</summary>
        Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes);

        /// <summary>Marks the user matching this activation token hash as EmailVerified and clears
        /// the token. Returns null if the hash matches nothing or the token already expired.</summary>
        Task<User?> UserActivateAsync(string tokenHash);

        /// <summary>Every admin-role user in the given tenant - used to notify a tenant's admins.
        /// Never empty for a real tenant: its creator always becomes its first admin.</summary>
        Task<IList<User>> TenantAdminsGetAsync(int tenantId);
    }
}
