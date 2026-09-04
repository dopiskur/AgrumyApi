using api.Models;

namespace api.Dal.Interface
{
    /// <summary>The minimal shape OfflineAlertBackgroundService needs to decide whether a
    /// notification is due - not the full Fleet dashboard DTO (DeviceFleetStatus), since
    /// OfflineNotifiedAt is alert-bookkeeping state the Web UI has no business displaying.</summary>
    public sealed record OfflineAlertCandidate(
        int IDDevice,
        int TenantID,
        string? DeviceName,
        int? SleepSeconds,
        DateTime? LastSeenAt,
        DateTime? OfflineNotifiedAt);

    /// <summary>The minimal shape LowBatteryAlertEvaluator needs - Battery is the latest
    /// sensorData row's reading (telemetry, not the heartbeat - see DeviceFleetStatus's class
    /// comment for why), null when the device has never reported one.</summary>
    public sealed record LowBatteryAlertCandidate(
        int IDDevice,
        int TenantID,
        string? DeviceName,
        int? Battery,
        DateTime? LowBatteryNotifiedAt);

    /// <summary>Device facet of the data layer: device CRUD, sensor/controller configs, firmware
    /// (OTA), the fixed type lists, and device events.</summary>
    public interface IDeviceRepository
    {
        /// <summary>Returns the created device (with its generated IDDevice) directly - callers don't need a follow-up DeviceGetAsync.</summary>
        Task<Device> DeviceAddAsync(Device device);

        Task DeviceDeleteAsync(int? idDevice, int? tenantID);

        /// <summary>The device matched by id / apiId / macAddress within the tenant, or null if none matches (or no key was given).</summary>
        Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress);

        /// <summary>The device with this id (no tenant filter) - used only for ownership checks before an authorized write - or null if none.</summary>
        Task<Device?> DeviceGetByIdAsync(int? idDevice);

        /// <summary>The device with this globally unique ApiId (no tenant filter), or null if none. Device-comm endpoints authenticate by ApiId/ApiKey and have no tenant context.</summary>
        Task<Device?> DeviceGetByApiIdAsync(string? apiId);
        Task<IList<Device>> DevicesGetAsync(int? tenantID);

        /// <summary>Every device in every tenant - callers must check CallerReadsDevicesGlobally themselves.</summary>
        Task<IList<Device>> DevicesGetAllAsync();
        Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress);
        Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID);
        Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID);

        /// <summary>The device owning this sensor/controller config id (no tenant filter) - for ownership checks before returning config data - or null if none.</summary>
        Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID);
        Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID);

        /// <summary>Newest published firmware for a device type (by DateAdded), or null if none.</summary>
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

        /// <summary>Records the diagnostics a device reported with its config poll - LastSeenAt is
        /// set to the server clock, making the poll itself the heartbeat. deviceID/tenantID come
        /// from the authenticated device identity, same rule as SensorDataPushAsync. Null
        /// diagnostic fields still bump LastSeenAt without erasing earlier values.</summary>
        Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll);

        /// <summary>Fleet status for every device in the tenant, or every device everywhere when
        /// tenantID is null - callers must check CallerReadsDevicesGlobally before passing null.
        /// Online is computed against the server clock via DeviceFleetStatus.ComputeOnline.</summary>
        Task<IList<DeviceFleetStatus>> DeviceFleetGetAsync(int? tenantID);

        // Device events

        /// <summary>Records one device event, unless an identical eventType for the same device was
        /// already recorded within the last ServerConfig.EventDedupeMinutes (default 10) - a
        /// flapping "NoInternet" every loop cycle should not flood the table. Returns false when the
        /// push was deduped (nothing written), true when it was actually inserted.</summary>
        Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message);

        /// <summary>Most recent events for one device, newest first, capped at <paramref name="limit"/>.
        /// tenantID is the caller's own tenant, not trusted from the request - a device belonging to
        /// another tenant simply matches zero rows rather than leaking another tenant's events.</summary>
        Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100);

        /// <summary>Marks one event acknowledged so it stops counting toward Unit/Zone Orange status,
        /// scoped by tenantID the same way EventDeviceGetAsync is (null only for a Global caller) -
        /// returns false rather than throwing when the id doesn't match (wrong tenant or already gone).</summary>
        Task<bool> EventDeviceAcknowledgeAsync(int idEventDevice, int? tenantID);

        // Offline alert background worker

        /// <summary>Every enabled device, across every tenant - the worker is not tenant-scoped,
        /// it runs once for the whole install.</summary>
        Task<IList<OfflineAlertCandidate>> OfflineAlertCandidatesGetAsync();

        /// <summary>Sets (or clears, notifiedAt: null) OfflineNotifiedAt on one device's diagnostic
        /// row - see OfflineAlertCandidate for what that field means.</summary>
        Task DeviceOfflineNotifiedSetAsync(int deviceID, DateTime? notifiedAt);

        // Low-battery alert background worker

        /// <summary>Every enabled device, across every tenant, with its latest telemetry battery
        /// reading - the worker is not tenant-scoped, it runs once for the whole install.</summary>
        Task<IList<LowBatteryAlertCandidate>> LowBatteryAlertCandidatesGetAsync();

        /// <summary>Sets (or clears, notifiedAt: null) LowBatteryNotifiedAt on one device's
        /// diagnostic row - see LowBatteryAlertCandidate for what that field means.</summary>
        Task DeviceLowBatteryNotifiedSetAsync(int deviceID, DateTime? notifiedAt);
    }
}
