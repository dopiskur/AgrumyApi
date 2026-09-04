namespace api.Dal.Entities
{
    // Persistence entities - mapped 1:1 to table columns. Kept separate from the api.Models DTOs,
    // which carry flattened join columns, MVC attributes and (for SensorData) no key. EfRepository
    // projects these rows onto the DTOs so the IRepository contract is unchanged.
    //
    // Column/type/nullability spec: this used to live in Schema/SchemaScripts.cs (deleted with the
    // stored-procedure DAL); the EF baseline migration is now the source of truth.

    public class TenantRow
    {
        public int IDTenant { get; set; }
        public string TenantName { get; set; } = "";
        public DateTime? DateCreated { get; set; }
    }

    public class UserRoleScopeRow
    {
        public int IDRoleScope { get; set; }
        public string? RoleScopeName { get; set; }
    }

    public class UserRoleRow
    {
        public int IDUserRole { get; set; }
        public string? RoleName { get; set; }
        public int? RoleScopeID { get; set; }
    }

    public class UserGroupRow
    {
        public int IDUserGroup { get; set; }
        public string? GroupName { get; set; }
        public int? UserRoleID { get; set; }
    }

    /// <summary>A user can hold several roles at once, so this many-to-many junction is the actual
    /// source of truth for authorization - UserGroupID/userGroup above stays untouched for backward
    /// compatibility (still drives the legacy single "admin"/"user" claim) but is no longer the
    /// only place a role lives.</summary>
    public class UserUserRoleRow
    {
        public int UserID { get; set; }
        public int UserRoleID { get; set; }
    }

    public class UserRow
    {
        public int IDUser { get; set; }
        public int TenantID { get; set; }
        public string Email { get; set; } = "";
        public string? Username { get; set; }
        // Null on the fresh-install bootstrap Global Admin seed row only - every other insert path
        // always supplies a real hash+salt. AuthenticationProvider.VerifyHash already treats a null
        // hash as "reject", so nothing can authenticate as this row until
        // BootstrapAdminSetPasswordAsync runs.
        public string? PwdHash { get; set; }
        public string? PwdSalt { get; set; }
        // Hash of the one-time bootstrap setup secret - set only on the seed row, cleared on success.
        public string? BootstrapSecretHash { get; set; }
        public string? BootstrapSecretSalt { get; set; }
        // 6-char generated code; null = never issued (or explicitly cleared).
        public string? DevicePin { get; set; }
        public DateTime? DevicePinExpires { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public int? UserGroupID { get; set; }
        public bool? Enabled { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        // Email ownership proof, separate from Enabled - EmailVerified is a second, independent
        // gate on top of it.
        public bool EmailVerified { get; set; }
        public string? ActivationTokenHash { get; set; }
        public DateTime? ActivationTokenExpiresAt { get; set; }

        // Resend-cooldown bookkeeping only - never surfaced on the public User DTO.
        public DateTime? ActivationLastSentAt { get; set; }

        // IANA zone id (e.g. "Europe/Zagreb"), never a raw UTC offset - offsets shift with DST,
        // TimeZoneInfo resolves the IANA id correctly year-round. Null = user never chose one,
        // presented as UTC (see api.Utils.TimeZoneHelper).
        public string? TimeZone { get; set; }
    }

    /// <summary>One issued JWT refresh token. Single-use: a rotation marks the row revoked and
    /// points ReplacedByTokenHash at the row that superseded it, so a reused (already-rotated)
    /// token is detectable. Only the hash is stored, never the plaintext token.</summary>
    public class RefreshTokenRow
    {
        public int IDRefreshToken { get; set; }
        public int UserID { get; set; }
        public string TokenHash { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByTokenHash { get; set; }
    }

    public class ServerConfigRow
    {
        public int IDServerConfig { get; set; }
        public string? ServerConfigName { get; set; }
        public string ConfigKey { get; set; } = "";
        public string? JWTKey { get; set; }
        public int? PortHTTP { get; set; }
        public int? PortHTTPS { get; set; }
        public string? ServerConfigCol { get; set; }

        // Server-wide hysteresis defaults - see api.Models.ServerConfig for the full story.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        public double? BatteryLowThreshold { get; set; }
        public double? BatteryLowHysteresis { get; set; }
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }
        public int? EventDedupeMinutes { get; set; }
        public int? ActivationResendCooldownMinutes { get; set; }
        public int? MaxRulesPerZone { get; set; }
        public bool AllowSelfServiceTenantCreation { get; set; }
        public bool TenantManagementEnabled { get; set; }
        public string? ScheduleTimeZone { get; set; }
        public int FirmwareSource { get; set; }
        public string? FirmwareGitHubRepository { get; set; }
        public string? FirmwareCustomRepositoryUrl { get; set; }
        public int? FirmwareRefreshIntervalHours { get; set; }
        public DateTime? FirmwareLastRefreshedAtUtc { get; set; }
        public int? SensorDataRetentionDays { get; set; }

        // See api.Models.ServerConfig's own copies of these for the full explanation.
        public double? WeatherLocationLat { get; set; }
        public double? WeatherLocationLon { get; set; }
        public int? WeatherPollIntervalMinutes { get; set; }
        public double? WeatherRainSkipThreshold { get; set; }
        public bool WeatherRainPredicted { get; set; }
        public DateTime? WeatherCheckedAtUtc { get; set; }
    }
}
