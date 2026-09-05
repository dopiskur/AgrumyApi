namespace api.Dal.Entities
{
    // Persistence entities mapped 1:1 to table columns, kept separate from the api.Models DTOs (flattened joins, MVC attributes) - EfRepository projects these onto the DTOs.

    public class TenantRow
    {
        public int IDTenant { get; set; }
        public string TenantName { get; set; } = "";
        public string? ScheduleTimeZone { get; set; } // See api.Models.Tenant.ScheduleTimeZone.
        public DateTime? DateCreated { get; set; }
    }

    /// See api.Models.TenantWifiConfig.
    public class TenantWifiConfigRow
    {
        public int IDTenantWifiConfig { get; set; }
        public int TenantID { get; set; }
        public string Ssid { get; set; } = "";
        public string Password { get; set; } = "";
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

    /// A user can hold several roles at once - this many-to-many junction is the sole source of truth for authorization.
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
        public string? PwdHash { get; set; } // Null only on the bootstrap seed row; AuthenticationProvider.VerifyHash rejects a null hash so it can't authenticate until BootstrapAdminSetPasswordAsync runs.
        public string? PwdSalt { get; set; }
        public string? BootstrapSecretHash { get; set; } // One-time bootstrap setup secret, set only on the seed row and cleared on success.
        public string? BootstrapSecretSalt { get; set; }
        public string? DevicePin { get; set; } // 6-char generated code; null = never issued or explicitly cleared.
        public DateTime? DevicePinExpires { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool? Enabled { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        public bool EmailVerified { get; set; } // Independent gate on top of Enabled, proving email ownership.
        public string? ActivationTokenHash { get; set; }
        public DateTime? ActivationTokenExpiresAt { get; set; }

        public DateTime? ActivationLastSentAt { get; set; } // Resend-cooldown bookkeeping only, never surfaced on the public User DTO.

        public string? TimeZone { get; set; } // IANA zone id, not a raw UTC offset, so TimeZoneInfo resolves it correctly across DST; null = presented as UTC (see api.Utils.TimeZoneHelper).

        public bool MustChangePassword { get; set; } // See api.Models.User.MustChangePassword.

        public DateTime? TokensValidAfterUtc { get; set; } // See api.Models.User.TokensValidAfterUtc.
    }

    /// One issued JWT refresh token - single-use, a rotation marks the row revoked and points ReplacedByTokenHash at its successor so a reused token is detectable; only the hash is stored.
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
        // ScheduleTimeZone is now per-tenant (see TenantRow.ScheduleTimeZone) - the serverConfig column is left in place, unmapped, rather than dropped.
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

        // See api.Models.ServerConfig's own copies of these for the full explanation.
        public bool GatewayEnabled { get; set; }
        public int GatewayMode { get; set; }
        public int GatewayWaitWindowSeconds { get; set; } = 30;

        // See api.Models.ServerConfig's own copies of these for the full explanation.
        public bool ProblemEventAlertsEnabled { get; set; } = true;
        public int ProblemEventExpiryHours { get; set; } = 24;

        // See api.Security.PasswordPolicy for the full explanation.
        public int PasswordMinLength { get; set; }
        public bool PasswordRequireComplexity { get; set; }

        // See api.Models.ServerConfig's own copy for the full explanation.
        public int ConfigHeartbeatHours { get; set; } = 24;

        // See api.Models.ServerConfig's own copies of these for the full explanation.
        public bool MqttTransportEnabled { get; set; }
        public string? MqttBrokerHost { get; set; }
        public int MqttBrokerPort { get; set; } = 1883;
        public string? MqttUsername { get; set; }
        public string? MqttPassword { get; set; }
    }
}
