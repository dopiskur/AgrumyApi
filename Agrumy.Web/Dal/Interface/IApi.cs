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

        // ---- Farm (roadmap #384) --------------------------

        [Get("/api/DeviceFarmUnit/Farm/All")]
        Task<IList<DeviceFarm>> DeviceFarmsGet();

        [Get("/api/DeviceFarmUnit/Farm")]
        Task<DeviceFarm> DeviceFarmGet(int? idDeviceFarm);

        [Post("/api/DeviceFarmUnit/Farm")]
        Task<DeviceFarm> DeviceFarmAdd([Body] DeviceFarm farm);

        [Put("/api/DeviceFarmUnit/Farm")]
        Task DeviceFarmUpdate([Body] DeviceFarm farm);

        [Delete("/api/DeviceFarmUnit/Farm")]
        Task DeviceFarmDelete(int? idDeviceFarm);

        // ---- Unit/Zone -----------------------------------

        [Get("/api/DeviceFarmUnit/All")]
        Task<IList<DeviceFarmUnit>> DeviceFarmUnitsGet();

        [Get("/api/DeviceFarmUnit")]
        Task<DeviceFarmUnit> DeviceFarmUnitGet(int? idDeviceFarmUnit);

        [Post("/api/DeviceFarmUnit")]
        Task<DeviceFarmUnit> DeviceFarmUnitAdd([Body] DeviceFarmUnit unit);

        [Put("/api/DeviceFarmUnit")]
        Task DeviceFarmUnitUpdate([Body] DeviceFarmUnit unit);

        [Delete("/api/DeviceFarmUnit")]
        Task DeviceFarmUnitDelete(int? idDeviceFarmUnit);

        [Get("/api/DeviceFarmUnit/Zone")]
        Task<IList<DeviceFarmUnitZone>> DeviceFarmUnitZonesGet(int? idDeviceFarmUnit);

        [Post("/api/DeviceFarmUnit/Zone")]
        Task<DeviceFarmUnitZone> DeviceFarmUnitZoneAdd([Body] DeviceFarmUnitZone zone);

        [Put("/api/DeviceFarmUnit/Zone")]
        Task DeviceFarmUnitZoneUpdate([Body] DeviceFarmUnitZone zone);

        [Delete("/api/DeviceFarmUnit/Zone")]
        Task DeviceFarmUnitZoneDelete(int? idDeviceFarmUnitZone);

        [Get("/api/DeviceFarmUnit/ZoneById")]
        Task<DeviceFarmUnitZone> DeviceFarmUnitZoneGetById(int? idDeviceFarmUnitZone);

        // ---- Rules (Zone/Unit/Global scope, roadmap #212) ------

        [Get("/api/DeviceFarmUnit/Zone/Rule")]
        Task<IList<DeviceFarmUnitZoneRule>> DeviceFarmUnitZoneRulesGet(int? idDeviceFarmUnitZone);

        [Post("/api/DeviceFarmUnit/Zone/Rule")]
        Task<int> DeviceFarmUnitZoneRuleAdd([Body] DeviceFarmUnitZoneRule rule);

        [Delete("/api/DeviceFarmUnit/Zone/Rule")]
        Task DeviceFarmUnitZoneRuleDelete(int? idDeviceFarmUnitZoneRule);

        [Get("/api/DeviceFarmUnit/Unit/Rule")]
        Task<IList<DeviceFarmUnitZoneRule>> DeviceFarmUnitRulesGet(int? idDeviceFarmUnit);

        [Post("/api/DeviceFarmUnit/Unit/Rule")]
        Task<int> DeviceFarmUnitRuleAdd([Body] DeviceFarmUnitZoneRule rule);

        [Delete("/api/DeviceFarmUnit/Unit/Rule")]
        Task DeviceFarmUnitRuleDelete(int? idDeviceFarmUnitZoneRule);

        [Get("/api/DeviceFarmUnit/Farm/Rule")]
        Task<IList<DeviceFarmUnitZoneRule>> DeviceFarmRulesGet(int? idDeviceFarm);

        [Post("/api/DeviceFarmUnit/Farm/Rule")]
        Task<int> DeviceFarmRuleAdd([Body] DeviceFarmUnitZoneRule rule);

        [Delete("/api/DeviceFarmUnit/Farm/Rule")]
        Task DeviceFarmRuleDelete(int? idDeviceFarmUnitZoneRule);

        [Get("/api/DeviceFarmUnit/Global/Rule")]
        Task<IList<DeviceFarmUnitZoneRule>> GlobalRulesGet();

        [Post("/api/DeviceFarmUnit/Global/Rule")]
        Task<int> GlobalRuleAdd([Body] DeviceFarmUnitZoneRule rule);

        [Delete("/api/DeviceFarmUnit/Global/Rule")]
        Task GlobalRuleDelete(int? idDeviceFarmUnitZoneRule);

        [Get("/api/DeviceFarmUnit/Unassigned")]
        Task<IList<DeviceDto>> DeviceUnassignedGet(bool controllerCapable);

        [Post("/api/DeviceFarmUnit/Assign")]
        Task DeviceAssign([Body] DeviceZoneAssignment body);

        [Post("/api/DeviceFarmUnit/Unassign")]
        Task DeviceUnassign(int? idDevice);

        [Get("/api/DeviceFarmUnit/Dashboard")]
        Task<IList<DeviceFarmUnitDashboard>> DeviceFarmUnitDashboardGet();

        [Get("/api/DeviceFarmUnit/Dashboard/Zones")]
        Task<IList<DeviceFarmUnitZoneDashboard>> DeviceFarmUnitZoneDashboardListGet(int? idDeviceFarmUnit);

        [Get("/api/DeviceFarmUnit/Dashboard/Zone")]
        Task<DeviceFarmUnitZoneDashboard> DeviceFarmUnitZoneDashboardGet(int? idDeviceFarmUnitZone);

        // ---- Manual actuate (roadmap #219) ---------------------

        [Post("/api/DeviceFarmUnit/Zone/ManualActuate")]
        Task<IReadOnlyList<int>> DeviceFarmUnitZoneManualActuateStart(int idDeviceFarmUnitZone, [Body] ManualActuateRequest request);

        [Post("/api/DeviceFarmUnit/Unit/ManualActuate")]
        Task<IReadOnlyList<int>> DeviceFarmUnitManualActuateStart(int idDeviceFarmUnit, [Body] ManualActuateRequest request);

        [Post("/api/DeviceFarmUnit/Zone/ManualActuate/Stop")]
        Task DeviceFarmUnitZoneManualActuateStop(int idDeviceFarmUnitZone, RelayFunction relayFunction);

        [Get("/api/DeviceFarmUnit/Zone/ManualActuate")]
        Task<IList<DeviceManualOverride>> DeviceFarmUnitZoneManualActuateStatus(int idDeviceFarmUnitZone);

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

        /// GlobalAdmin-only, see DeviceApiController.HardResetRequest - wipes the device on its next reachable poll (normal or, if its apiKey is broken, the apiId-only HardResetPending path).
        [Post("/api/Device/HardReset")]
        Task DeviceHardReset(int idDevice);

        // ---- SensorData ---------------------------------------------------

        [Get("/api/SensorData")]
        Task<string> SensorDataGet(int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        /// Same JSON shape as SensorDataGet, time-bucket averaged across every device in the zone/unit.
        [Get("/api/SensorData/ZoneAverage")]
        Task<string> SensorDataZoneAverageGet(int deviceFarmUnitZoneID, int? timeRange, int? timeMDMY);

        [Get("/api/SensorData/UnitAverage")]
        Task<string> SensorDataUnitAverageGet(int deviceFarmUnitID, int? timeRange, int? timeMDMY);

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

        /// SENSITIVE (password hashes, device ApiKeys - see TenantApiController.Export) - ZIP bytes, the Web controller streams them straight to the browser, never writing them to disk.
        [Get("/api/Tenant/Export")]
        Task<HttpResponseMessage> TenantExport(int idTenant, bool includeSensorData = false, DateTime? sensorDataSinceUtc = null);

        [Post("/api/Tenant/Import")]
        Task<TenantImportResult> TenantImport([Body] TenantImportRequest value);

        /// Anonymous (see TenantApiController.ImportAsSentinel) - reachable from the same SetupAdmin screen BootstrapPending/BootstrapSetPassword already use.
        [Post("/api/Tenant/ImportAsSentinel")]
        Task<TenantImportResult> TenantImportAsSentinel([Body] TenantExport value);

        // ---- Server config --------------------------------

        [Get("/api/ServerConfig")]
        Task<ServerConfig> ServerConfigGet();

        /// Sends through the SAVED Email settings, not the unsaved form - see ServerConfigApiController.TestEmail.
        [Post("/api/ServerConfig/TestEmail")]
        Task ServerConfigTestEmail(string toEmail);

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
        Task<List<AuditLogEntry>> AuditLogGet(int take = 200, string? actorEmail = null, string? action = null, string? targetType = null, DateTime? fromUtc = null, DateTime? toUtc = null);
    }
}
