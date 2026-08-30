namespace api.Security
{
    /// <summary>Roadmap #66's role catalog. A user can hold several of these at once (see the
    /// userUserRole junction table, EfRepository.UserRoleNamesGetAsync/UserRolesSetAsync) - these
    /// constants exist so the exact RoleName strings seeded by the migration and checked in
    /// [Authorize(Roles=...)]/CallerHasRole never drift apart by a typo.</summary>
    public static class RoleNames
    {
        // Base roles - exactly one per scope a user belongs to. Admin already implies every
        // capability at that scope, so it is never combined with the grants below.
        public const string GlobalAdmin = "Global admin";
        public const string GlobalReader = "Global reader";
        public const string TenantAdmin = "Tenant admin";
        public const string TenantReader = "Tenant reader";

        // Composable grants - zero or more, layered on top of a *Reader base.
        public const string GlobalUser = "Global User";
        public const string GlobalDevice = "Global Device";
        public const string TenantUser = "Tenant User";
        public const string TenantDevice = "Tenant Device";

        /// <summary>Every role introduced by roadmap #66, in the order the migration seeds them.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            GlobalAdmin, GlobalReader, GlobalUser, GlobalDevice,
            TenantAdmin, TenantReader, TenantUser, TenantDevice,
        };

        // Pre-#66 role names. Still what every [Authorize(Roles="admin")] check across the ~34
        // untouched call sites looks for (roadmap #66 Phase 2) - CreateToken derives and adds one
        // of these alongside the real role set below so nothing built before Phase 2 breaks.
        public const string LegacyAdmin = "admin";
        public const string LegacyUser = "user";

        /// <summary>True if holding this role set would have been "admin" under the pre-#66 model
        /// - used only to pick the legacy alias claim, never for a real authorization decision.</summary>
        public static bool ImpliesLegacyAdmin(IEnumerable<string> roleNames) =>
            roleNames.Contains(GlobalAdmin) || roleNames.Contains(TenantAdmin);
    }
}
