using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    public interface IRepository
    {
        // Startup / health

        /// <summary>Opens and immediately closes a database connection. Returns true if the connection could be opened.</summary>
        Task<bool> TestConnectionAsync();

        /// <summary>Ensures the schema exists: if the key table is missing, runs the batches from Schema/SchemaScripts.cs.</summary>
        Task EnsureSchemaAsync();

        /// <summary>Classifies a database-layer exception so callers can return a consistent error response. CPU-only, stays synchronous.</summary>
        DbFailureKind ClassifyException(Exception ex);

        // Server Config

        Task<ServerConfig> ServerConfigGetAsync(int idServerConfig);



        // MANAGE USER
        Task UserAddAsync(User user, UserSecret userHash);
        Task UserUpdateAsync(User user);
        Task<bool> UserDeleteAsync(int? idUser);
        Task<User> UserGetAsync(int? idUser, string? email, string? username);
        Task<IList<User>> UsersGetAsync(int? tenantID);
        Task<UserSecret> UserSecretGetAsync(int? idUser, string? email, string? username);

        Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret);

        Task<IList<UserRole>> UserRoleGetAsync();

        // MANAGE DEVICE

        Task DeviceAddAsync(Device device);

        Task DeviceDeleteAsync(int? idDevice, int? tenantID);
        Task<Device> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress);

        /// <summary>Fetches a device by id only, with no tenant filter - used only to check device ownership before an authorized write, never to serve data directly to a caller.</summary>
        Task<Device> DeviceGetByIdAsync(int? idDevice);
        Task<IList<Device>> DevicesGetAsync(int? tenantID);
        Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress);
        Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID);
        Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID);

        /// <summary>Fetches the device that owns this sensor/controller config id, with no tenant filter - used only for ownership checks before returning config data.</summary>
        Task<Device> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID);
        Task<Device> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID);

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

        Task SensorDataPushAsync(JsonArray jsonArray);
        Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);
        Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? getData, int? deviceID, int? sensorDataReportID);
        Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);


        // Tenant
        Task<bool> TenantGetAsync(string tenantName);
        Task<int?> TenantGetIdAsync(string tenantName);
        Task<int> TenantAddAsync(string tenantName);

        // Group
        Task<IList<UserGroup>> UserGroupsGetAsync();
        Task<UserGroup> UserGroupGetAsync(int? idUserGroup);
        Task UserGroupDeleteAsync(int? idUserGroup);
        Task UserGroupAddAsync(UserGroup userGroup);
    }
}
