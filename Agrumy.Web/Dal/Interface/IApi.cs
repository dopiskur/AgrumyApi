using api.Models;
using Refit;

namespace api.Dal.Interface
{
    /// Declarative Refit client for Agrumy.Api (AddRefitClient in Program.cs) - BearerTokenHandler attaches the JWT so no method takes a token param, and non-success responses raise ApiException (RefitConfig) carrying the response body.
    public interface IApi
    {
        // ---- User ----------------------------------------------------------

        [Post("/api/User/Login")]
        Task<UserLoginResult?> UserLogin([Body] UserLogin userLogin);

        /// Tenant-import counterpart to Login - proves identity with the old (imported) password since MustChangePassword blocks Login itself (428, api.Models.User.MustChangePassword).
        [Post("/api/User/ForceChangePassword")]
        Task<UserLoginResult?> UserForceChangePassword([Body] UserForceChangePassword value);

        /// Whether the bootstrap Global Admin still has no password - LoginController checks this on every anonymous page load to pick the login form or the first-run "set password" screen.
        [Get("/api/User/BootstrapPending")]
        Task<bool> BootstrapPending();

        /// The one-shot call that gives the bootstrap Global Admin a real password - see api.Models.BootstrapAdminSetPassword for why it takes no login/email.
        [Post("/api/User/BootstrapSetPassword")]
        Task BootstrapSetPassword([Body] BootstrapAdminSetPassword value);

        // Task (not Task<User>) on purpose: no caller reads the created record, same convention as the other write endpoints.
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

        /// The caller's own record (self-scoped by the JWT server-side) - profile page prefill and the display time zone for UTC-to-local conversion.
        [Get("/api/User/Self")]
        Task<User> UserGetSelf();

        /// Self-service profile write - FirstName/LastName/TimeZone only, identity from the attached JWT (see UserApiController.UserProfileSet).
        [Put("/api/User/Profile")]
        Task UserProfileSet([Body] UserProfileUpdate value);

        /// Password-change flow - proves the caller still knows the old password, identity otherwise comes from the attached JWT.
        [Post("/api/User/ChangePassword")]
        Task ChangePassword([Body] UserSetPassword value);

        /// Rotates the caller's device-registration PIN (24h validity, multi-use - not consumed by the first successful registration).
        [Post("/api/User/DevicePin")]
        Task<DevicePinResult> DevicePinGenerate();

        [Get("/api/User/Roles")]
        Task<IEnumerable<UserRole>> UserRoleGet();

        /// The given user's composable role set (a user can hold several).
        [Get("/api/User/UserRoles")]
        Task<List<string>> UserRolesGet(int idUser);

        [Put("/api/User/UserRoles")]
        Task UserRolesSet([Body] UserRolesUpdate value);

        // ---- Device --------------------------------------------------------

        [Get("/api/Device/All")]
        Task<IEnumerable<DeviceDto>> DevicesGet();

        [Get("/api/Device")]
        Task<DeviceDto> DeviceGet(int? idDevice);

        [Put("/api/Device")]
        Task DeviceUpdate([Body] DeviceDto? device);

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

        [Get("/api/Device/Role")]
        Task<IEnumerable<DeviceRole>> DeviceRoleGet();

        [Get("/api/Device/Type")]
        Task<IEnumerable<DeviceType>> DeviceTypeGet();

        [Post("/api/Simulation/Device")]
        Task<DeviceDto> SimulationDeviceCreate();

        [Get("/api/Simulation/Device")]
        Task<IList<int>> SimulationDeviceList();

        [Delete("/api/Simulation/Device/{idDevice}")]
        Task SimulationDeviceDelete(int idDevice);

        [Get("/api/Device/Simulation/{idDevice}")]
        Task<DeviceSimulation> DeviceSimulationGet(int idDevice);

        [Put("/api/Device/Simulation/{idDevice}")]
        Task DeviceSimulationSet(int idDevice, [Body] DeviceSimulation value);

        [Get("/api/Device/TypeService")]
        Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet();

        [Get("/api/Device/TypeRelay")]
        Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet();

        [Get("/api/Device/TypeSensor")]
        Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet();

        // ---- Device events -----------------------------------

        [Get("/api/Device/Events")]
        Task<IList<DeviceEvent>> DeviceEventsGet(int? idDevice);

        [Put("/api/Device/Event/{idEventDevice}/Acknowledge")]
        Task DeviceEventAcknowledge(int idEventDevice);

        // ---- Fleet dashboard ----------------------------------

        [Get("/api/Device/Fleet")]
        Task<IList<DeviceFleetStatus>> DeviceFleetGet();

        [Get("/api/Device/FleetStatus")]
        Task<DeviceFleetStatus> DeviceFleetStatusGet(int idDevice);

        // ---- Gateway ------------------------------------------

        [Get("/api/Gateway/All")]
        Task<IList<DeviceDto>> GatewaysGetAll();

        [Get("/api/Gateway/DeviceMapping/All")]
        Task<IList<GatewayDeviceMapping>> GatewayDeviceMappingGetAll(int idGatewayDevice);

        [Post("/api/Gateway/DeviceMapping")]
        Task GatewayDeviceMappingAdd([Body] GatewayDeviceMapping value);

        [Delete("/api/Gateway/DeviceMapping")]
        Task GatewayDeviceMappingDelete(int idGatewayDeviceMapping, int idGatewayDevice);

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

        // ---- Rules (Zone/Unit/Global scope, roadmap #212) ------

        [Get("/api/DeviceUnit/Zone/Rule")]
        Task<IList<DeviceUnitZoneRule>> DeviceUnitZoneRulesGet(int? idDeviceUnitZone);

        [Post("/api/DeviceUnit/Zone/Rule")]
        Task<int> DeviceUnitZoneRuleAdd([Body] DeviceUnitZoneRule rule);

        [Delete("/api/DeviceUnit/Zone/Rule")]
        Task DeviceUnitZoneRuleDelete(int? idDeviceUnitZoneRule);

        [Get("/api/DeviceUnit/Unit/Rule")]
        Task<IList<DeviceUnitZoneRule>> DeviceUnitRulesGet(int? idDeviceUnit);

        [Post("/api/DeviceUnit/Unit/Rule")]
        Task<int> DeviceUnitRuleAdd([Body] DeviceUnitZoneRule rule);

        [Delete("/api/DeviceUnit/Unit/Rule")]
        Task DeviceUnitRuleDelete(int? idDeviceUnitZoneRule);

        [Get("/api/DeviceUnit/Global/Rule")]
        Task<IList<DeviceUnitZoneRule>> GlobalRulesGet();

        [Post("/api/DeviceUnit/Global/Rule")]
        Task<int> GlobalRuleAdd([Body] DeviceUnitZoneRule rule);

        [Delete("/api/DeviceUnit/Global/Rule")]
        Task GlobalRuleDelete(int? idDeviceUnitZoneRule);

        [Get("/api/DeviceUnit/Unassigned")]
        Task<IList<DeviceDto>> DeviceUnassignedGet(bool controllerCapable);

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

        // ---- Manual actuate (roadmap #219) ---------------------

        [Post("/api/DeviceUnit/Zone/ManualActuate")]
        Task<IReadOnlyList<int>> DeviceUnitZoneManualActuateStart(int idDeviceUnitZone, [Body] ManualActuateRequest request);

        [Post("/api/DeviceUnit/Unit/ManualActuate")]
        Task<IReadOnlyList<int>> DeviceUnitManualActuateStart(int idDeviceUnit, [Body] ManualActuateRequest request);

        [Post("/api/DeviceUnit/Zone/ManualActuate/Stop")]
        Task DeviceUnitZoneManualActuateStop(int idDeviceUnitZone, RelayFunction relayFunction);

        [Get("/api/DeviceUnit/Zone/ManualActuate")]
        Task<IList<DeviceManualOverride>> DeviceUnitZoneManualActuateStatus(int idDeviceUnitZone);

        // ---- Device commands ---------------------------------

        [Post("/api/DeviceCommand")]
        Task<IReadOnlyList<int>> DeviceCommandIssue([Body] IssueCommandRequest request);

        // ---- Discovery ----------------

        [Post("/api/Discovery/Scan")]
        Task<IReadOnlyList<int>> DiscoveryScan([Body] DiscoveryScanRequest request);

        [Get("/api/Discovery/Results")]
        Task<IList<DiscoveryResult>> DiscoveryResultsGet(int? unitID, int? zoneID);

        [Get("/api/Discovery/WifiConfigs")]
        Task<IList<TenantWifiConfig>> DiscoveryWifiConfigsGet();

        [Post("/api/Discovery/WifiConfigs")]
        Task<TenantWifiConfig> DiscoveryWifiConfigAdd([Body] TenantWifiConfig config);

        [Put("/api/Discovery/WifiConfigs/{idTenantWifiConfig}")]
        Task DiscoveryWifiConfigUpdate(int idTenantWifiConfig, [Body] TenantWifiConfig config);

        [Delete("/api/Discovery/WifiConfigs/{idTenantWifiConfig}")]
        Task DiscoveryWifiConfigDelete(int idTenantWifiConfig);

        [Post("/api/Discovery/Register")]
        Task<DiscoveryRegisterResult> DiscoveryRegister([Body] DiscoveryRegisterRequest request);

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

        [Multipart]
        [Post("/api/Firmware/UploadZip")]
        Task<FirmwareSyncResult> FirmwareUploadZip([AliasAs("file")] StreamPart file);

        /// A ZIP of the visible catalog + manifest.json, streamed through so the browser download proxies through Agrumy.Web like every other admin action.
        [Get("/api/Firmware/DownloadZip")]
        Task<HttpResponseMessage> FirmwareDownloadZip(bool latestOnly);

        /// Raw .bin bytes of one catalog entry, streamed through the API whatever its source - the Flash Device tab's same-origin path to a GitHub asset.
        [Get("/api/Firmware/Fetch")]
        Task<HttpResponseMessage> FirmwareFetch(string fileName);

        [Post("/api/Device/FirmwareUpdate")]
        Task DeviceFirmwareUpdate([Body] DeviceFirmwareUpdateRequest request);

        [Delete("/api/Device/FirmwareUpdate")]
        Task DeviceFirmwareUpdateCancel(int idDevice);

        [Post("/api/Device/WifiUpdate")]
        Task DeviceWifiUpdate([Body] DeviceWifiUpdateRequest request);

        // ---- SensorData ---------------------------------------------------

        [Get("/api/SensorData")]
        Task<string> SensorDataGet(int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        /// Same JSON shape as SensorDataGet, time-bucket averaged across every device in the zone/unit.
        [Get("/api/SensorData/ZoneAverage")]
        Task<string> SensorDataZoneAverageGet(int deviceUnitZoneID, int? timeRange, int? timeMDMY);

        [Get("/api/SensorData/UnitAverage")]
        Task<string> SensorDataUnitAverageGet(int deviceUnitID, int? timeRange, int? timeMDMY);

        [Get("/api/SensorData/Report")]
        Task<IEnumerable<SensorDataReport>> SensorDataReportGet(int? idDevice, int? iDSensorDataReport, int? getData);

        // ---- Tenant -------------------------------------------------------

        [Get("/api/Tenant/All")]
        Task<IEnumerable<Tenant>> TenantsGet();

        [Get("/api/Tenant")]
        Task<Tenant> TenantGet(int idTenant);

        [Post("/api/Tenant")]
        Task<int> TenantAdd([Body] Tenant tenant);

        [Put("/api/Tenant")]
        Task TenantUpdate([Body] Tenant tenant);

        [Get("/api/Tenant/EmergencyStop")]
        Task<bool> EmergencyStopStatus(int? idTenant = null);

        [Post("/api/Tenant/EmergencyStop")]
        Task EmergencyStopActivate(int? idTenant = null);

        [Post("/api/Tenant/EmergencyStop/Clear")]
        Task EmergencyStopClear(int? idTenant = null);

        /// SENSITIVE (password hashes, device ApiKeys - see TenantApiController.Export) - the Web controller streams this straight to the browser, never writing it to disk.
        [Get("/api/Tenant/Export")]
        Task<TenantExport> TenantExport(int idTenant, bool includeSensorData = false, DateTime? sensorDataSinceUtc = null);

        [Post("/api/Tenant/Import")]
        Task<TenantImportResult> TenantImport([Body] TenantImportRequest value);

        /// Anonymous (see TenantApiController.ImportAsSentinel) - reachable from the same SetupAdmin screen BootstrapPending/BootstrapSetPassword already use.
        [Post("/api/Tenant/ImportAsSentinel")]
        Task<TenantImportResult> TenantImportAsSentinel([Body] TenantExport value);

        // ---- Server config --------------------------------

        [Get("/api/ServerConfig")]
        Task<ServerConfig> ServerConfigGet();

        [Put("/api/ServerConfig")]
        Task ServerConfigUpdate([Body] ServerConfig config);

        /// The one ServerConfig field an anonymous page (Register) is allowed to read - whether to show the "create a new tenant" option at all.
        [Get("/api/ServerConfig/Public")]
        Task<PublicServerConfig> ServerConfigGetPublic();

        // ---- Data maintenance -----------------------------

        /// Whether the current DB provider is MariaDB/MySQL - decides whether the Purge confirmation flow needs the extra "shrink files on disk?" dialog at all.
        [Get("/api/DataMaintenance/Provider")]
        Task<DataMaintenanceProviderInfo> DataMaintenanceProviderGet();

        [Post("/api/DataMaintenance/Optimize")]
        Task DataMaintenanceOptimize([Body] DataMaintenanceRequest request);

        [Post("/api/DataMaintenance/Purge")]
        Task DataMaintenancePurge([Body] DataPurgeRequest request);

        // ---- Audit log --------------------------------------

        [Get("/api/AuditLog")]
        Task<List<AuditLogEntry>> AuditLogGet(int take = 200);
    }
}
