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

        /// <summary>The caller's own record (self-scoped by the JWT server-side) - profile page
        /// prefill and the display time zone for UTC-to-local conversion (roadmap #71 follow-up).</summary>
        [Get("/api/User/Self")]
        Task<User> UserGetSelf();

        /// <summary>Self-service profile write - FirstName/LastName/TimeZone only, identity from
        /// the attached JWT (see Agrumy.Api's UserApiController.UserProfileSet).</summary>
        [Put("/api/User/Profile")]
        Task UserProfileSet([Body] UserProfileUpdate value);

        /// <summary>Existing password-change flow that proves identity with the old password -
        /// reused by the profile page rather than a parallel mechanism.</summary>
        [Post("/api/User/ChangePassword")]
        Task ChangePassword([Body] UserSetPassword value);

        /// <summary>Roadmap #70: rotates the caller's single-use device-registration PIN (24h
        /// validity, consumed by the first successful device registration).</summary>
        [Post("/api/User/DevicePin")]
        Task<DevicePinResult> DevicePinGenerate();

        [Get("/api/User/Roles")]
        Task<IEnumerable<UserRole>> UserRoleGet();

        /// <summary>Roadmap #66: the given user's composable role set (a user can hold several).</summary>
        [Get("/api/User/UserRoles")]
        Task<List<string>> UserRolesGet(int idUser);

        [Put("/api/User/UserRoles")]
        Task UserRolesSet([Body] UserRolesUpdate value);

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

        // ---- Device events (roadmap #28) -----------------------------------

        [Get("/api/Device/Events")]
        Task<IList<DeviceEvent>> DeviceEventsGet(int? idDevice);

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

        // ---- Server config (roadmap #10) --------------------------------

        [Get("/api/ServerConfig")]
        Task<ServerConfig> ServerConfigGet();

        [Put("/api/ServerConfig")]
        Task ServerConfigUpdate([Body] ServerConfig config);

        /// <summary>Roadmap #64: the one ServerConfig field an anonymous page (Register) is allowed
        /// to read - whether to show the "create a new tenant" option at all.</summary>
        [Get("/api/ServerConfig/Public")]
        Task<PublicServerConfig> ServerConfigGetPublic();
    }
}
