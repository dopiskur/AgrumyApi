using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    public interface IRepository
    {
        // Startup / health

        /// <summary>Opens and immediately closes a database connection. Returns true if the connection could be opened.</summary>
        Task<bool> TestConnectionAsync();

        /// <summary>Ensures the schema exists: on an empty database, applies the EF Core baseline migration; a database that already has tables is left untouched.</summary>
        Task EnsureSchemaAsync();

        /// <summary>Classifies a database-layer exception so callers can return a consistent error response. CPU-only, stays synchronous.</summary>
        DbFailureKind ClassifyException(Exception ex);

        // Server Config

        Task<ServerConfig> ServerConfigGetAsync(int idServerConfig);
        Task ServerConfigUpdateAsync(ServerConfig config);

        /// <summary>Overwrites the DB row's hysteresis fields from appsettings.json (roadmap #10) -
        /// only called at startup when Config.serverConfigReload is true.</summary>
        Task ServerConfigReloadFromAppSettingsAsync(int idServerConfig);



        // MANAGE USER
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);

        /// <summary>Self-service profile write - only FirstName/LastName/TimeZone, never any
        /// authorization-bearing column (see EfRepository.UserProfileSetAsync). False if no such user.</summary>
        Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone);

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

        // Refresh tokens - opaque, single-use, rotated on every redemption. Only a SHA-256 hash of
        // the token ever reaches the DB or these method signatures.
        Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt);

        /// <summary>The token identified by its hash, or null if no such token was ever issued.</summary>
        Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash);

        /// <summary>Atomically revokes <paramref name="oldTokenHash"/> (pointing it at the new hash,
        /// for reuse-chain tracking) and inserts the new token row. No-op if the old token is missing
        /// or already revoked - the caller is expected to have checked that first.</summary>
        Task RefreshTokenRotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt);

        /// <summary>Revokes one token (explicit logout). Idempotent - revoking an unknown or
        /// already-revoked token is not an error.</summary>
        Task RefreshTokenRevokeAsync(string tokenHash);

        /// <summary>Revokes every active token for a user - the response to detecting reuse of an
        /// already-rotated token (signals the token was stolen, so every session dies, not just the
        /// one that got caught).</summary>
        Task RefreshTokenRevokeAllForUserAsync(int userID);

        // MANAGE DEVICE

        Task DeviceAddAsync(Device device);

        Task DeviceDeleteAsync(int? idDevice, int? tenantID);

        /// <summary>The device matched by id / apiId / macAddress within the tenant, or null if none matches (or no key was given).</summary>
        Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress);

        /// <summary>The device with this id (no tenant filter) - used only for ownership checks before an authorized write - or null if none.</summary>
        Task<Device?> DeviceGetByIdAsync(int? idDevice);

        /// <summary>The device with this globally unique ApiId (no tenant filter), or null if none. Device-comm endpoints authenticate by ApiId/ApiKey and have no tenant context.</summary>
        Task<Device?> DeviceGetByApiIdAsync(string? apiId);
        Task<IList<Device>> DevicesGetAsync(int? tenantID);

        /// <summary>Every device in every tenant - #66 Phase 2, callers must check CallerReadsDevicesGlobally themselves.</summary>
        Task<IList<Device>> DevicesGetAllAsync();
        Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress);
        Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID);
        Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID);

        /// <summary>The device owning this sensor/controller config id (no tenant filter) - for ownership checks before returning config data - or null if none.</summary>
        Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID);
        Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID);

        /// <summary>Newest published firmware for a device type (by DateAdded), or null if none. Roadmap #3 (OTA).</summary>
        Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID);

        // Device UPDATE
        Task DeviceUpdateAsync(Device? device);
        Task DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? deviceConfigController);
        Task DeviceConfigSensorUpdateAsync(int? iDDevice, DeviceConfigSensor? deviceConfigSensor);

        // Device fixed lists
        Task<IList<DeviceType>> DeviceTypeGetAsync();
        Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync();
        Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync();
        Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync();



        // SensorData

        /// <summary>
        /// Persist a telemetry batch. deviceID/tenantID/deviceUnitID/deviceUnitZoneID come from the
        /// authenticated device identity and are applied to every row; the same keys inside each JSON
        /// object are ignored, so a device cannot write telemetry against another device or tenant.
        /// </summary>
        Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID);
        Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);
        Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? sensorDataReportID);
        Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);

        // Device events (roadmap #28)

        /// <summary>
        /// Records one device event, unless an identical eventType for the same device was already
        /// recorded within the last ServerConfig.EventDedupeMinutes (default 10) - a flapping
        /// "NoInternet" every loop cycle should not flood the table. deviceID/tenantID come from the
        /// authenticated device identity, same rule as SensorDataPushAsync. Returns false when the
        /// push was deduped (nothing written), true when it was actually inserted.
        /// </summary>
        Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message);

        /// <summary>Most recent events for one device, newest first, capped at <paramref name="limit"/>.
        /// tenantID is the caller's own tenant, not trusted from the request - a device belonging to
        /// another tenant simply matches zero rows rather than leaking another tenant's events.</summary>
        Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100);

        // Tenant
        Task<bool> TenantGetAsync(string tenantName);
        Task<int?> TenantGetIdAsync(string tenantName);
        Task<int> TenantAddAsync(string tenantName);

        // Group
        Task<IList<UserGroup>> UserGroupsGetAsync();

        /// <summary>The group matched by id, or null if none matches.</summary>
        Task<UserGroup?> UserGroupGetAsync(int? idUserGroup);
        Task UserGroupDeleteAsync(int? idUserGroup);
        Task UserGroupAddAsync(UserGroup userGroup);
    }
}
