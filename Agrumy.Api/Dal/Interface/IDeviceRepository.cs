using api.Models;

namespace api.Dal.Interface
{
    /// The minimal shape OfflineAlertBackgroundService needs - not the full DeviceFleetStatus, since OfflineNotifiedAt is alert-bookkeeping the Web UI has no business displaying.
    public sealed record OfflineAlertCandidate(
        int IDDevice,
        int TenantID,
        string? DeviceName,
        int? SleepSeconds,
        DateTime? LastSeenAt,
        DateTime? OfflineNotifiedAt);

    /// The minimal shape LowBatteryAlertEvaluator needs - Battery is the latest telemetry reading (not the heartbeat, see DeviceFleetStatus), null when never reported.
    public sealed record LowBatteryAlertCandidate(
        int IDDevice,
        int TenantID,
        string? DeviceName,
        int? Battery,
        DateTime? LowBatteryNotifiedAt);

    /// Device facet of the data layer: device CRUD, sensor/controller configs, firmware (OTA), the fixed type lists, and device events.
    public interface IDeviceRepository
    {
        /// Returns the created device (with its generated IDDevice) directly - callers don't need a follow-up DeviceGetAsync.
        Task<Device> DeviceAddAsync(Device device);

        Task DeviceDeleteAsync(int? idDevice, int? tenantID);

        /// The device matched by id / apiId / macAddress within the tenant, or null if none matches (or no key was given).
        Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress);

        /// The device with this id (no tenant filter) - used only for ownership checks before an authorized write - or null if none.
        Task<Device?> DeviceGetByIdAsync(int? idDevice);

        /// The device with this globally unique ApiId (no tenant filter), or null if none. Device-comm endpoints authenticate by ApiId/ApiKey and have no tenant context.
        Task<Device?> DeviceGetByApiIdAsync(string? apiId);
        Task<IList<Device>> DevicesGetAsync(int? tenantID);

        /// Every device in every tenant - callers must check CallerReadsDevicesGlobally themselves.
        Task<IList<Device>> DevicesGetAllAsync();

        /// Every sensor-only device (DeviceSensorEnabled, not DeviceControllerEnabled) in the tenant, or every tenant when null - excludes controller-capable devices since a WiFi.scanNetworks() pause would disrupt their real-time relay duties.
        Task<IList<Device>> DevicesSensorOnlyGetAsync(int? tenantID);
        Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress);
        Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID);
        Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID);

        /// The device owning this sensor/controller config id (no tenant filter) - for ownership checks before returning config data - or null if none.
        Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID);
        Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID);

        /// Newest published firmware for a device type (by DateAdded), or null if none.
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

        // Device diagnostics / fleet

        /// Records the diagnostics from a device's config poll - LastSeenAt is set to the server clock, making the poll itself the heartbeat; null fields still bump LastSeenAt without erasing earlier values.
        Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll);

        /// Stamps the device row with when a full DeviceConfig body was actually sent - drives DeviceConfigBuilder.NeedsRefreshAsync's periodic heartbeat resend (ServerConfig.ConfigHeartbeatHours).
        Task DeviceMarkConfigSentAsync(int deviceID, DateTime sentAtUtc);

        /// Fleet status for every device in the tenant, or everywhere when tenantID is null (caller must check CallerReadsDevicesGlobally first) - Online comes from DeviceFleetStatus.ComputeOnline.
        Task<IList<DeviceFleetStatus>> DeviceFleetGetAsync(int? tenantID);

        /// Same shape as one DeviceFleetGetAsync row, scoped to a single device - null if deviceID doesn't exist or (when tenantID is set) belongs to another tenant.
        Task<DeviceFleetStatus?> DeviceFleetStatusGetAsync(int deviceID, int? tenantID);

        // Device events

        /// Skips (returns false) an identical eventType for the same device within ServerConfig.EventDedupeMinutes - a flapping "NoInternet" every loop cycle must not flood the table.
        Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message);

        /// Most recent events for one device, newest first, capped at <paramref name="limit"/> - tenantID is the caller's own, so a cross-tenant device just matches zero rows rather than leaking events.
        Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100);

        /// Marks one event acknowledged so it stops counting toward Unit/Zone Orange status - returns false rather than throwing when the id doesn't match (wrong tenant or already gone).
        Task<bool> EventDeviceAcknowledgeAsync(int idEventDevice, int? tenantID);

        // Offline alert background worker

        /// Every enabled device across every tenant - the worker is not tenant-scoped, it runs once for the whole install.
        Task<IList<OfflineAlertCandidate>> OfflineAlertCandidatesGetAsync();

        /// Sets (or clears, notifiedAt: null) OfflineNotifiedAt on one device's diagnostic row.
        Task DeviceOfflineNotifiedSetAsync(int deviceID, DateTime? notifiedAt);

        // Low-battery alert background worker

        /// Every enabled device across every tenant with its latest battery reading - not tenant-scoped, runs once for the whole install.
        Task<IList<LowBatteryAlertCandidate>> LowBatteryAlertCandidatesGetAsync();

        /// Sets (or clears, notifiedAt: null) LowBatteryNotifiedAt on one device's diagnostic row.
        Task DeviceLowBatteryNotifiedSetAsync(int deviceID, DateTime? notifiedAt);
    }
}
