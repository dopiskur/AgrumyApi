using api.Models;

namespace api.Dal.Interface
{
    /// <summary>User facet of the data layer (roadmap #74): accounts, secrets, composable roles,
    /// email activation, and the legacy userGroup mapping (kept here because groups exist solely
    /// as the pre-#66 role assignment for users).</summary>
    public interface IUserRepository
    {
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);

        /// <summary>Self-service profile write - only FirstName/LastName/TimeZone, never any
        /// authorization-bearing column (see EfRepository.UserProfileSetAsync). False if no such user.</summary>
        Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone);

        /// <summary>Roadmap #70: sole writer of the device-registration PIN - nulls consume it
        /// (successful registration), a value+expiry (re)issues it. False if no such user.</summary>
        Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc);

        Task<bool> UserDeleteAsync(int? idUser);

        /// <summary>The user matched by id / email / username, or null if none matches (or no key was given).</summary>
        Task<User?> UserGetAsync(int? idUser, string? email, string? username);
        Task<IList<User>> UsersGetAsync(int? tenantID);

        /// <summary>Every user in every tenant - roadmap #65, callers must enforce the global-admin check themselves.</summary>
        Task<IList<User>> UsersGetAllAsync();

        /// <summary>The password hash+salt for the user matched by id / email / username, or null if none matches.</summary>
        Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username);

        Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret);

        Task<IList<UserRole>> UserRoleGetAsync();

        // Roadmap #66: a user can hold several roles at once - the userUserRole junction table is
        // the source of truth for this set, independent of the legacy single UserGroupID/userGroup.

        /// <summary>Every role name currently assigned to this user via userUserRole. Empty (never
        /// null) for a user nobody has migrated/assigned yet.</summary>
        Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser);

        /// <summary>Replaces this user's ENTIRE role set with exactly <paramref name="roleNames"/> -
        /// not incremental. Unknown role names are silently ignored (defensive - the Web UI only
        /// ever offers api.Security.RoleNames.All as choices).</summary>
        Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames);

        // Email activation (roadmap #24)

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

        /// <summary>Every admin-role user in the given tenant - roadmap #63's "notify the tenant's
        /// admins" step. Never empty for a real tenant: its creator always becomes its first admin.</summary>
        Task<IList<User>> TenantAdminsGetAsync(int tenantId);

        // Group (legacy pre-#66 role mapping)

        Task<IList<UserGroup>> UserGroupsGetAsync();

        /// <summary>The group matched by id, or null if none matches.</summary>
        Task<UserGroup?> UserGroupGetAsync(int? idUserGroup);
        Task UserGroupDeleteAsync(int? idUserGroup);
        Task UserGroupAddAsync(UserGroup userGroup);
    }
}
