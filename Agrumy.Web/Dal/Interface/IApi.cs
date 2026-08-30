using api.Models;
using Refit;

namespace api.Dal.Interface
{
    /// <summary>
    /// Declarative client for Agrumy.Api (roadmap #32). Implemented at runtime by Refit
    /// (<c>AddRefitClient&lt;IApi&gt;</c> in Program.cs). The caller's JWT is attached by
    /// <see cref="api.Security.BearerTokenHandler"/>, so no method takes a token parameter.
    /// Nullable query parameters are omitted when null. Non-success responses raise
    /// <see cref="api.Utils.ApiException"/> carrying the response body (see
    /// <see cref="api.Utils.RefitConfig"/>).
    /// </summary>
    public interface IApi
    {
        // ---- User ----------------------------------------------------------

        [Post("/api/User/Login")]
        Task<UserLoginResult?> UserLogin([Body] UserLogin userLogin);

        [Post("/api/User/Register")]
        Task<User> UserRegister([Body] UserRegistration registration);

        [Post("/api/User")]
        Task UserAdd([Body] UserAdd user);

        [Put("/api/User")]
        Task UserUpdate([Body] UserUpdate userUpdate);

        [Delete("/api/User")]
        Task UserDelete(int? idUser);

        [Get("/api/User")]
        Task<User> UserGet(int? idUser);

        [Get("/api/User/All")]
        Task<IEnumerable<User>> UsersGet();

        [Get("/api/User/Roles")]
        Task<IEnumerable<UserRole>> UserRoleGet();

        // ---- Device --------------------------------------------------------

        [Get("/api/Device/All")]
        Task<IEnumerable<Device>> DevicesGet();

        [Get("/api/Device")]
        Task<Device> DeviceGet(int? idDevice);

        [Put("/api/Device")]
        Task DeviceUpdate([Body] Device? device);

        [Delete("/api/Device")]
        Task DeviceDelete(int? idDevice);

        [Get("/api/Device/Sensor")]
        Task<DeviceConfigSensor> DeviceConfigSensorGet(int? deviceConfigSensorID);

        [Get("/api/Device/Controller")]
        Task<DeviceConfigController> DeviceConfigControllerGet(int? deviceConfigControllerID);

        [Put("/api/Device/Sensor")]
        Task DeviceConfigSensorUpdate([Body] DeviceUpdate deviceUpdate);

        [Put("/api/Device/Controller")]
        Task DeviceConfigControllerUpdate([Body] DeviceUpdate deviceUpdate);

        [Get("/api/Device/Type")]
        Task<IEnumerable<DeviceType>> DeviceTypeGet();

        [Get("/api/Device/TypeService")]
        Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet();

        [Get("/api/Device/TypeRelay")]
        Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet();

        [Get("/api/Device/TypeSensor")]
        Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet();

        // ---- SensorData ---------------------------------------------------

        [Get("/api/SensorData")]
        Task<string> SensorDataGet(int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        [Get("/api/SensorData/Report")]
        Task<IEnumerable<SensorDataReport>> SensorDataReportGet(int? idDevice, int? iDSensorDataReport, int? getData);

        // ---- Group ------------------------------------------------------

        [Get("/api/User/Group/All")]
        Task<IEnumerable<UserGroup>> UserGroupsGet();

        [Get("/api/User/Group")]
        Task<UserGroup> UserGroupGet(int idUserGroup);

        [Post("/api/User/Group")]
        Task UserGroupAdd([Body] UserGroup userGroup);

        [Delete("/api/User/Group")]
        Task UserGroupDelete(int? idUserGroup);
    }
}
