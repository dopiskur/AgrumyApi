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



        // MANAGE USER
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);
        Task<bool> UserDeleteAsync(int? idUser);

        /// <summary>The user matched by id / email / username, or null if none matches (or no key was given).</summary>
        Task<User?> UserGetAsync(int? idUser, string? email, string? username);
        Task<IList<User>> UsersGetAsync(int? tenantID);

        /// <summary>The password hash+salt for the user matched by id / email / username, or null if none matches.</summary>
        Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username);

        Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret);

        Task<IList<UserRole>> UserRoleGetAsync();

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
