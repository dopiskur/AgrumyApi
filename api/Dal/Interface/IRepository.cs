using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    public interface IRepository
    {
        // Server Config

        ServerConfig ServerConfigGet(int idServerConfig);



        // MANAGE USER
        void UserAdd(User user, UserSecret userHash);
        void UserUpdate(User user);
        bool UserDelete(int? idUser);
        User UserGet(int? idUser, string? email, string? username);
        IList<User> UsersGet(int? tenantID);
        UserSecret UserSecretGet(int? idUser, string? email, string? username);

        bool UserSetPassword(string? email, UserSecret userSecret);

        IList<UserRole> UserRoleGet();

        // MANAGE DEVICE

        void DeviceAdd(Device device);

        void DeviceDelete(int? idDevice);
        Device DeviceGet(int? tenantID, int? idDevice, string? apiId, string? macAddress);
        IList<Device> DevicesGet(int? tenantID);
        bool DeviceCheckMacAddress(int? tenantID, string? macAddress);
        DeviceConfigSensor? DeviceConfigSensorGet(int? deviceConfigSensorID);
        DeviceConfigController? DeviceConfigControllerGet(int? deviceConfigControllerID);

        // Device UPDATE
        void DeviceUpdate(Device? device);
        void DeviceConfigControllerUpdate(int? idDevice, DeviceConfigController? deviceConfigController);
        void DeviceConfigSensorUpdate(int? iDDevice, DeviceConfigSensor? deviceConfigSensor);

        // Device fixed lists
        IList<DeviceType> DeviceTypeGet();
        IList<DeviceTypeService> DeviceTypeServiceGet();
        IList<DeviceTypeRelay> DeviceTypeRelayGet();
        IList<DeviceTypeSensor> DeviceTypeSensorGet();



        // SensorData

        void SensorDataPush(JsonArray jsonArray);
        string SensorDataGet(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);
        IList<SensorDataReport> SensorDataReportGet(int? getData, int? deviceID, int? sensorDataReportID);
        void SensorDataDelete(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);


        // Tenant
        public bool TenantGet(string tenantName);
        int TenantAdd(string tenantName);

        // Group
        IList<UserGroup> UserGroupsGet();
        UserGroup UserGroupGet(int? idUserGroup);
        void UserGroupDelete(int? idUserGroup);
        void UserGroupAdd(UserGroup userGroup);
    }
}
