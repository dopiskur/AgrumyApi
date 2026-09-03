namespace api.Security
{
    /// <summary>Role catalog; a user can hold several at once. These constants exist so the RoleName strings seeded by the migration and checked in [Authorize(Roles=...)]/CallerHasRole never drift apart by a typo.</summary>
    public static class RoleNames
    {
        // Base roles - exactly one per scope a user belongs to. Admin already implies every capability at that scope, so it is never combined with the grants below.
        public const string GlobalAdmin = "Global admin";
        public const string GlobalReader = "Global reader";
        public const string TenantAdmin = "Tenant admin";
        public const string TenantReader = "Tenant reader";

        // Composable grants - zero or more, layered on top of a *Reader base.
        public const string GlobalUser = "Global User";
        public const string GlobalDevice = "Global Device";
        public const string TenantUser = "Tenant User";
        public const string TenantDevice = "Tenant Device";

        public static readonly IReadOnlyList<string> All = new[]
        {
            GlobalAdmin, GlobalReader, GlobalUser, GlobalDevice,
            TenantAdmin, TenantReader, TenantUser, TenantDevice,
        };

        // Legacy role names - still what every [Authorize(Roles="admin")] check across older call sites looks for; CreateToken derives and adds one of these alongside the real role set so nothing built before the multi-role model breaks.
        public const string LegacyAdmin = "admin";
        public const string LegacyUser = "user";

        /// <summary>True if holding this role set would have been "admin" under the legacy model - used only to pick the legacy alias claim, never for a real authorization decision.</summary>
        public static bool ImpliesLegacyAdmin(IEnumerable<string> roleNames) =>
            roleNames.Contains(GlobalAdmin) || roleNames.Contains(TenantAdmin);

        // Comma-separated lists for [Authorize(Roles = ...)] - any listed role passes the attribute (the coarse gate); the precise per-tenant decision happens inline via ApiControllerBase's capability helpers. LegacyAdmin is included so an account the multi-role migration missed keeps its old access instead of being locked out.

        /// <summary>May manage user accounts (create/edit/delete) - tenant scoping still applies inline.</summary>
        public const string UserManagers = LegacyAdmin + "," + GlobalAdmin + "," + GlobalUser + "," + TenantAdmin + "," + TenantUser;

        /// <summary>May manage devices and their configs - tenant scoping still applies inline.</summary>
        public const string DeviceManagers = LegacyAdmin + "," + GlobalAdmin + "," + GlobalDevice + "," + TenantAdmin + "," + TenantDevice;

        // Role GRANTING stays admin-only on purpose: a Tenant User could otherwise assign themselves Tenant admin.
        public const string Admins = LegacyAdmin + "," + GlobalAdmin + "," + TenantAdmin;
    }
}
