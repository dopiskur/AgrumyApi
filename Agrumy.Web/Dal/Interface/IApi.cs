using api.Models;
using Refit;

namespace api.Dal.Interface
{
    /// <summary>
    /// Declarative client for Agrumy.Api (roadmap #32). Implemented at runtime by Refit -
    /// registered in Program.cs via <c>AddRefitClient&lt;IApi&gt;</c>, which supplies the
    /// IHttpClientFactory-managed <see cref="HttpClient"/>. The caller's JWT is attached per
    /// request with <c>[Authorize("Bearer")]</c>; nullable query parameters are omitted when null.
    /// Non-success responses raise <see cref="api.Utils.ApiException"/> carrying the response body
    /// (see <see cref="api.Utils.RefitConfig"/>).
    /// </summary>
    public interface IApi
    {
        // ---- User ----------------------------------------------------------

        [Post("/api/User/Login")]
        Task<UserLoginResult?> UserLogin([Body] UserLogin userLogin);

        [Post("/api/User")]
        Task UserAdd([Authorize("Bearer")] string jwtKey, [Body] UserAdd user);

        [Put("/api/User")]
        Task UserUpdate([Authorize("Bearer")] string jwtKey, [Body] UserUpdate userUpdate);

        [Delete("/api/User")]
        Task UserDelete([Authorize("Bearer")] string jwtKey, int? idUser);

        [Get("/api/User")]
        Task<User> UserGet([Authorize("Bearer")] string? jwtKey, int? idUser, string? email, string? username);

        [Get("/api/User/All")]
        Task<IEnumerable<User>> UsersGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/User/Roles")]
        Task<IEnumerable<UserRole>> UserRoleGet([Authorize("Bearer")] string jwtKey);

        // ---- Device --------------------------------------------------------

        [Get("/api/Device/All")]
        Task<IEnumerable<Device>> DevicesGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/Device")]
        Task<Device> DeviceGet([Authorize("Bearer")] string jwtKey, int? idDevice, string? apiId, string? macAddress);

        [Put("/api/Device")]
        Task DeviceUpdate([Authorize("Bearer")] string jwtKey, [Body] Device? device);

        [Delete("/api/Device")]
        Task DeviceDelete([Authorize("Bearer")] string jwtKey, int? idDevice);

        [Get("/api/Device/Sensor")]
        Task<DeviceConfigSensor> DeviceConfigSensorGet([Authorize("Bearer")] string jwtKey, int? deviceConfigSensorID);

        [Get("/api/Device/Controller")]
        Task<DeviceConfigController> DeviceConfigControllerGet([Authorize("Bearer")] string jwtKey, int? deviceConfigControllerID);

        [Put("/api/Device/Sensor")]
        Task DeviceConfigSensorUpdate([Authorize("Bearer")] string jwtKey, [Body] DeviceUpdate deviceUpdate);

        [Put("/api/Device/Controller")]
        Task DeviceConfigControllerUpdate([Authorize("Bearer")] string jwtKey, [Body] DeviceUpdate deviceUpdate);

        [Get("/api/Device/Type")]
        Task<IEnumerable<DeviceType>> DeviceTypeGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/Device/TypeService")]
        Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/Device/TypeRelay")]
        Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/Device/TypeSensor")]
        Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet([Authorize("Bearer")] string jwtKey);

        // ---- SensorData ---------------------------------------------------

        [Get("/api/SensorData")]
        Task<string> SensorDataGet([Authorize("Bearer")] string jwtKey, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        [Get("/api/SensorData/Report")]
        Task<IEnumerable<SensorDataReport>> SensorDataReportGet([Authorize("Bearer")] string? jwtKey, int? idDevice, int? iDSensorDataReport, int? getData);

        // ---- Group ------------------------------------------------------

        [Get("/api/User/Group/All")]
        Task<IEnumerable<UserGroup>> UserGroupsGet([Authorize("Bearer")] string jwtKey);

        [Get("/api/User/Group")]
        Task<UserGroup> UserGroupGet([Authorize("Bearer")] string jwtKey, int idUserGroup);

        [Post("/api/User/Group")]
        Task UserGroupAdd([Authorize("Bearer")] string jwtKey, [Body] UserGroup userGroup);

        [Delete("/api/User/Group")]
        Task UserGroupDelete([Authorize("Bearer")] string jwtKey, int? idUserGroup);
    }
}
