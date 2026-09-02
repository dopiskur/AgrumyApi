using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Roadmap #40: the minimal shape OfflineAlertBackgroundService needs to decide
    /// whether a notification is due - not the full Fleet dashboard DTO (DeviceFleetStatus),
    /// since OfflineNotifiedAt is alert-bookkeeping state the Web UI has no business displaying.
    /// TenantID mirrors DeviceRow.TenantID, non-nullable since roadmap #112.</summary>
    public sealed record OfflineAlertCandidate(
        int IDDevice,
        int TenantID,
        string? DeviceName,
        int? SleepSeconds,
        DateTime? LastSeenAt,
        DateTime? OfflineNotifiedAt);

    /// <summary>Device facet of the data layer (roadmap #74): device CRUD, sensor/controller
    /// configs, firmware (OTA, roadmap #3), the fixed type lists, and device events (roadmap #28).</summary>
    public interface IDeviceRepository
    {
        Task DeviceAddAsync(Device device);

        Task DeviceDeleteAsync(int? idDevice, int? tenantID);

        /// <summary>The device matched by id / apiId / macAddress within the tenant, or null if none matches (or no key was given).</summary>
        Task<Device?> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress);

        /// <summary>The device with this id (no tenant filter) - used only for ownership checks before an authorized write - or null if none.</summary>
        Task<Device?> DeviceGetByIdAsync(int? idDevice);

        /// <summary>The device with this globally unique ApiId (no tenant filter), or null if none. Device-comm endpoints authenticate by ApiId/ApiKey and have no tenant context.</summary>
        Task<Device?> DeviceGetByApiIdAsync(string? apiId);
        Task<IList<Device>> DevicesGetAsync(int? tenantID);

        /// <summary>Every device in every tenant - #66 Phase 2, callers must check CallerReadsDevicesGlobally themselves.</summary>
        Task<IList<Device>> DevicesGetAllAsync();
        Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress);
        Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID);
        Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID);

        /// <summary>The device owning this sensor/controller config id (no tenant filter) - for ownership checks before returning config data - or null if none.</summary>
        Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID);
        Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID);

        /// <summary>Newest published firmware for a device type (by DateAdded), or null if none. Roadmap #3 (OTA).</summary>
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

        // Device diagnostics / fleet (roadmap #7 + #8)

        /// <summary>
        /// Records the diagnostics a device reported with its config poll (roadmap #7) - LastSeenAt
        /// is set to the server clock, making the poll itself the heartbeat. deviceID/tenantID come
        /// from the authenticated device identity, same rule as SensorDataPushAsync (#47). Null
        /// diagnostic fields (pre-#7 firmware) still bump LastSeenAt without erasing earlier values.
        /// </summary>
        Task DeviceDiagnosticUpsertAsync(int deviceID, int tenantID, DeviceConfigPoll poll);

        /// <summary>Fleet status for every device in the tenant, or every device everywhere when
        /// tenantID is null (roadmap #8) - callers must check CallerReadsDevicesGlobally before
        /// passing null. Online is computed against the server clock via DeviceFleetStatus.ComputeOnline.</summary>
        Task<IList<DeviceFleetStatus>> DeviceFleetGetAsync(int? tenantID);

        // Device events (roadmap #28)

        /// <summary>
        /// Records one device event, unless an identical eventType for the same device was already
        /// recorded within the last ServerConfig.EventDedupeMinutes (default 10) - a flapping
        /// "NoInternet" every loop cycle should not flood the table. deviceID/tenantID come from the
        /// authenticated device identity, same rule as SensorDataPushAsync. Returns false when the
        /// push was deduped (nothing written), true when it was actually inserted.
        /// </summary>
        Task<bool> EventDevicePushAsync(int deviceID, int tenantID, DeviceEventType eventType, string? message);

        /// <summary>Most recent events for one device, newest first, capped at <paramref name="limit"/>.
        /// tenantID is the caller's own tenant, not trusted from the request - a device belonging to
        /// another tenant simply matches zero rows rather than leaking another tenant's events.</summary>
        Task<IList<DeviceEvent>> EventDeviceGetAsync(int? deviceID, int? tenantID, int limit = 100);

        // Offline alert background worker (roadmap #40)

        /// <summary>Every enabled device, across every tenant - the worker is not tenant-scoped,
        /// it runs once for the whole install.</summary>
        Task<IList<OfflineAlertCandidate>> OfflineAlertCandidatesGetAsync();

        /// <summary>Sets (or clears, notifiedAt: null) OfflineNotifiedAt on one device's diagnostic
        /// row - see OfflineAlertCandidate for what that field means.</summary>
        Task DeviceOfflineNotifiedSetAsync(int deviceID, DateTime? notifiedAt);
    }
}
