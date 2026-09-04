using api.Models;
using Refit;

namespace api.Dal.Interface
{
    /// <summary>Declarative client for Agrumy.Api. Implemented at runtime by Refit
    /// (<c>AddRefitClient&lt;IApi&gt;</c> in Program.cs). The caller's JWT is attached by
    /// <see cref="api.Security.BearerTokenHandler"/>, so no method takes a token parameter.
    /// Nullable query parameters are omitted when null. Non-success responses raise
    /// <see cref="api.Utils.ApiException"/> carrying the response body (see
    /// <see cref="api.Utils.RefitConfig"/>).</summary>
    public interface IApi
    {
        // ---- User ----------------------------------------------------------

        [Post("/api/User/Login")]
        Task<UserLoginResult?> UserLogin([Body] UserLogin userLogin);

        /// <summary>Whether the fresh-install bootstrap Global Admin still has no password -
        /// LoginController checks this on every anonymous page load to decide whether to show the
        /// normal login form or the first-run "set password" screen.</summary>
        [Get("/api/User/BootstrapPending")]
        Task<bool> BootstrapPending();

        /// <summary>The one-shot call that gives the bootstrap Global Admin a real password - see
        /// api.Models.BootstrapAdminSetPassword for why it takes no login/email.</summary>
        [Post("/api/User/BootstrapSetPassword")]
        Task BootstrapSetPassword([Body] BootstrapAdminSetPassword value);

        // Task (not Task<User>) on purpose: no caller reads the created record, same convention
        // as the other write endpoints whose response bodies nobody consumed.
        [Post("/api/User/Register")]
        Task UserRegister([Body] UserRegistration registration);

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
        /// prefill and the display time zone for UTC-to-local conversion.</summary>
        [Get("/api/User/Self")]
        Task<User> UserGetSelf();

        /// <summary>Self-service profile write - FirstName/LastName/TimeZone only, identity from
        /// the attached JWT (see Agrumy.Api's UserApiController.UserProfileSet).</summary>
        [Put("/api/User/Profile")]
        Task UserProfileSet([Body] UserProfileUpdate value);

        /// <summary>Password-change flow: proves the caller still knows the old password, identity
        /// otherwise comes from the attached JWT - reused by the profile page rather than a
        /// parallel mechanism.</summary>
        [Post("/api/User/ChangePassword")]
        Task ChangePassword([Body] UserSetPassword value);

        /// <summary>Rotates the caller's device-registration PIN (24h validity, multi-use - not
        /// consumed by the first successful device registration).</summary>
        [Post("/api/User/DevicePin")]
        Task<DevicePinResult> DevicePinGenerate();

        [Get("/api/User/Roles")]
        Task<IEnumerable<UserRole>> UserRoleGet();

        /// <summary>The given user's composable role set (a user can hold several).</summary>
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

        // ---- Device events -----------------------------------

        [Get("/api/Device/Events")]
        Task<IList<DeviceEvent>> DeviceEventsGet(int? idDevice);

        // ---- Fleet dashboard ----------------------------------

        [Get("/api/Device/Fleet")]
        Task<IList<DeviceFleetStatus>> DeviceFleetGet();

        // ---- Unit/Zone -----------------------------------

        [Get("/api/DeviceUnit/All")]
        Task<IList<DeviceUnit>> DeviceUnitsGet();

        [Get("/api/DeviceUnit")]
        Task<DeviceUnit> DeviceUnitGet(int? idDeviceUnit);

        [Post("/api/DeviceUnit")]
        Task<DeviceUnit> DeviceUnitAdd([Body] DeviceUnit unit);

        [Put("/api/DeviceUnit")]
        Task DeviceUnitUpdate([Body] DeviceUnit unit);

        [Delete("/api/DeviceUnit")]
        Task DeviceUnitDelete(int? idDeviceUnit);

        [Get("/api/DeviceUnit/Zone")]
        Task<IList<DeviceUnitZone>> DeviceUnitZonesGet(int? idDeviceUnit);

        [Post("/api/DeviceUnit/Zone")]
        Task<DeviceUnitZone> DeviceUnitZoneAdd([Body] DeviceUnitZone zone);

        [Put("/api/DeviceUnit/Zone")]
        Task DeviceUnitZoneUpdate([Body] DeviceUnitZone zone);

        [Delete("/api/DeviceUnit/Zone")]
        Task DeviceUnitZoneDelete(int? idDeviceUnitZone);

        [Get("/api/DeviceUnit/ZoneById")]
        Task<DeviceUnitZone> DeviceUnitZoneGetById(int? idDeviceUnitZone);

        // ---- Zone rules ---------------------------------------

        [Get("/api/DeviceUnit/Zone/Rule")]
        Task<IList<DeviceUnitZoneRule>> DeviceUnitZoneRulesGet(int? idDeviceUnitZone);

        [Post("/api/DeviceUnit/Zone/Rule")]
        Task<int> DeviceUnitZoneRuleAdd([Body] DeviceUnitZoneRule rule);

        [Delete("/api/DeviceUnit/Zone/Rule")]
        Task DeviceUnitZoneRuleDelete(int? idDeviceUnitZoneRule);

        [Get("/api/DeviceUnit/Unassigned")]
        Task<IList<Device>> DeviceUnassignedGet(bool controllerCapable);

        [Post("/api/DeviceUnit/Assign")]
        Task DeviceAssign([Body] DeviceZoneAssignment body);

        [Post("/api/DeviceUnit/Unassign")]
        Task DeviceUnassign(int? idDevice);

        [Get("/api/DeviceUnit/Dashboard")]
        Task<IList<DeviceUnitDashboard>> DeviceUnitDashboardGet();

        [Get("/api/DeviceUnit/Dashboard/Zones")]
        Task<IList<DeviceUnitZoneDashboard>> DeviceUnitZoneDashboardListGet(int? idDeviceUnit);

        [Get("/api/DeviceUnit/Dashboard/Zone")]
        Task<DeviceUnitZoneDashboard> DeviceUnitZoneDashboardGet(int? idDeviceUnitZone);

        // ---- Device commands ---------------------------------

        [Post("/api/DeviceCommand")]
        Task<IReadOnlyList<int>> DeviceCommandIssue([Body] IssueCommandRequest request);

        // ---- Firmware catalog + per-device update ----

        [Get("/api/Firmware")]
        Task<IList<DeviceFirmware>> FirmwareList(string? board);

        [Post("/api/Firmware/Sync")]
        Task<FirmwareSyncResult> FirmwareSync([Body] FirmwareSyncRequest request);

        [Post("/api/Firmware/Import")]
        Task<FirmwareSyncResult> FirmwareImport([Body] FirmwareImportRequest request);

        [Multipart]
        [Post("/api/Firmware/Upload")]
        Task<DeviceFirmware> FirmwareUpload([AliasAs("file")] StreamPart file);

        [Delete("/api/Firmware")]
        Task FirmwareDelete(int idDeviceFirmware);

        [Get("/api/Firmware/Manifest")]
        Task<FirmwareManifest> FirmwareManifest();

        /// <summary>Raw .bin bytes of one catalog entry, streamed through the API whatever its source
        /// - the browser "Build offline repo" tool's same-origin path to a GitHub asset.</summary>
        [Get("/api/Firmware/Fetch")]
        Task<HttpResponseMessage> FirmwareFetch(string fileName);

        [Post("/api/Device/FirmwareUpdate")]
        Task DeviceFirmwareUpdate([Body] DeviceFirmwareUpdateRequest request);

        [Delete("/api/Device/FirmwareUpdate")]
        Task DeviceFirmwareUpdateCancel(int idDevice);

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

        // ---- Tenant -------------------------------------------------------

        [Get("/api/Tenant/All")]
        Task<IEnumerable<Tenant>> TenantsGet();

        [Get("/api/Tenant")]
        Task<Tenant> TenantGet(int idTenant);

        [Post("/api/Tenant")]
        Task<int> TenantAdd([Body] Tenant tenant);

        [Put("/api/Tenant")]
        Task TenantUpdate([Body] Tenant tenant);

        // ---- Server config --------------------------------

        [Get("/api/ServerConfig")]
        Task<ServerConfig> ServerConfigGet();

        [Put("/api/ServerConfig")]
        Task ServerConfigUpdate([Body] ServerConfig config);

        /// <summary>The one ServerConfig field an anonymous page (Register) is allowed to read -
        /// whether to show the "create a new tenant" option at all.</summary>
        [Get("/api/ServerConfig/Public")]
        Task<PublicServerConfig> ServerConfigGetPublic();

        // ---- Data maintenance -----------------------------

        /// <summary>Whether the current DB provider is MariaDB/MySQL - decides whether the Purge
        /// confirmation flow needs the extra "shrink files on disk?" dialog at all.</summary>
        [Get("/api/DataMaintenance/Provider")]
        Task<DataMaintenanceProviderInfo> DataMaintenanceProviderGet();

        [Post("/api/DataMaintenance/Optimize")]
        Task DataMaintenanceOptimize([Body] DataMaintenanceRequest request);

        [Post("/api/DataMaintenance/Purge")]
        Task DataMaintenancePurge([Body] DataPurgeRequest request);
    }
}
