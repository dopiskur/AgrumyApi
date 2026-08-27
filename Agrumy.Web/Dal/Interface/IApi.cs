using api.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    public interface IApi
    {


        // MANAGE USER
        public Task<UserLoginResult>? UserLogin(UserLogin userLogin);

        public Task<bool> UserAdd(string jwtKey, UserAdd user);
        public Task<bool> UserUpdate(string jwtKey, UserUpdate userUpdate);
        public Task<bool> UserDelete(string jwtKey, int? idUser);
        public Task<User> UserGet(string? jwtKey, int? idUser, string? email, string? username);
        public Task<IEnumerable<User>> UsersGet(string jwtKey);
        public Task<IEnumerable<UserRole>> UserRoleGet(string jwtKey);



        // MANAGE DEVICE
        public Task<IEnumerable<Device>> DevicesGet(string jwtKey);
        public Task<Device> DeviceGet(string jwtKey, int? idDevice, string? apiId, string? macAddress);
        public Task<bool> DeviceUpdate(string jwtKey, Device? device);
        public Task<bool> DeviceDelete(string jwtKey, int? idDevice);
        public Task<DeviceConfigSensor> DeviceConfigSensorGet(string jwtKey, int? deviceConfigSensorID);
        public Task<DeviceConfigController> DeviceConfigControllerGet(string jwtKey, int? deviceConfigControllerID);

        public Task<bool> DeviceConfigSensorUpdate(string jwtKey, DeviceUpdate deviceUpdate);
        public Task<bool> DeviceConfigControllerUpdate(string jwtKey, DeviceUpdate deviceUpdate);



        // Device fixed lists
        public Task<IEnumerable<DeviceType>> DeviceTypeGet(string jwtKey);
        public Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet(string jwtKey);
        public Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet(string jwtKey);
        public Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet(string jwtKey);

        // SensorData
        public Task<string> SensorDataGet(string jwtKey, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);
        public Task<IEnumerable<SensorDataReport>> SensorDataReportGet(string? jwtKey, int? idDevice, int? iDSensorDataReport, int? getData);

        // Group
        public Task <IEnumerable<UserGroup>> UserGroupsGet(string jwtKey);
        public Task<UserGroup> UserGroupGet(string jwtKey, int idUserGroup);
        public Task<bool> UserGroupAdd(string jwtKey, UserGroup userGroup);
        public Task<bool> UserGroupDelete(string jwtKey, int? idUserGroup);

    }
}
