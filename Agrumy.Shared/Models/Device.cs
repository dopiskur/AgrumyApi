using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace api.Models
{
    public class Device
    {
        public int? ConfigVersion { get; set; } = 1;
        // See api.Models.DeviceConfig.CommandVersion for the full story.
        public int? CommandVersion { get; set; }

        [HiddenInput(DisplayValue = true)]
        public int? IDDevice { get; set; }
        // Non-nullable - TenantID=0 is a real default tenant, not a "no tenant" sentinel; nullable would let an impossible third state leak into consumers.
        [HiddenInput(DisplayValue = true)]
        public int TenantID { get; set; } = 0;

        public int? DeviceRoleID { get; set; } = 0;
        // No default (unlike DeviceRoleID above) - null means genuinely unassigned, not a 0-as-sentinel value.
        [HiddenInput(DisplayValue = true)]
        public int? DeviceUnitID { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? DeviceUnitZoneID { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? DeviceConfigSensorID { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? DeviceConfigControllerID { get; set; }

        public int? DeviceTypeServiceID { get; set; } = 0;

        public string? DeviceName { get; set; }
        [HiddenInput(DisplayValue = true)]
        public string? MacAddress { get; set; }
        // True only for an Agrumy.Gateway instance, flagged so GatewayApiController.Batch can tell a gateway's own credential from an ordinary device's.
        [HiddenInput(DisplayValue = true)]
        public bool IsGateway { get; set; }
        // Null for non-gateway devices; set once at registration (DeviceRegistration.GatewayProfile), not editable afterward since Profile A/B imply different physical setups.
        [HiddenInput(DisplayValue = true)]
        public GatewayProfile? GatewayProfile { get; set; }
        // The device's actual bearer credential - never serialized out; BuildDeviceConfigAsync reads it directly in C# instead.
        [JsonIgnore]
        public string? ApiId { get; set; }
        [JsonIgnore]
        public string? ApiKey { get; set; }
        public string? ServicePoint { get; set; }

        public string? ServiceType {  get; set; }

        public string? ServicePublicKey { get; set; }

        public int? SleepSeconds { get; set; } = 60;
        public bool? SleepDeepEnabled { get; set; } = false;

        [HiddenInput(DisplayValue = true)]
        public bool? DeviceSensorEnabled { get; set; } = false;
        [HiddenInput(DisplayValue = true)]
        public bool? DeviceControllerEnabled { get; set; } = false;
        public bool? BatteryEnabled { get; set; } = false;

        public bool? Debug { get; set; } = true;
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; } = false;
        public bool? FirmwareUpdate { get; set; }
        // Null = latest-for-board when FirmwareUpdate is set; a specific version pins rollback/downgrade. Both cleared by DeviceApiController.GetConfig once the heartbeat confirms that version.
        public string? FirmwareTargetVersion { get; set; }
        public bool? Enabled { get; set; } = false;


        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        // Written only when GetConfig/RunConfigAsync actually sends a full DeviceConfig body - drives the ConfigHeartbeatHours periodic resend (see DeviceConfigBuilder.NeedsRefreshAsync); never exposed via DeviceDto, purely internal bookkeeping.
        public DateTime? LastFullConfigSentAt { get; set; }
    }

    /// The only shape of a device that ever crosses the HTTP boundary in either direction (GET responses, PUT /api/Device body) - identical to Device minus ApiId/ApiKey, which stay internal to EfRepository/DeviceConfigBuilder no matter what future fields get added here.
    public class DeviceDto
    {
        public int? ConfigVersion { get; set; } = 1;
        public int? CommandVersion { get; set; }
        public int? IDDevice { get; set; }
        public int TenantID { get; set; } = 0;
        public int? DeviceRoleID { get; set; } = 0;
        // No default (unlike DeviceRoleID above) - null means genuinely unassigned, see Device.DeviceUnitID.
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public int? DeviceConfigSensorID { get; set; }
        public int? DeviceConfigControllerID { get; set; }
        public int? DeviceTypeServiceID { get; set; } = 0;
        public string? DeviceName { get; set; }
        public string? MacAddress { get; set; }
        public bool IsGateway { get; set; }
        public GatewayProfile? GatewayProfile { get; set; }
        public string? ServicePoint { get; set; }
        public string? ServiceType { get; set; }
        public string? ServicePublicKey { get; set; }
        public int? SleepSeconds { get; set; } = 60;
        public bool? SleepDeepEnabled { get; set; } = false;
        public bool? DeviceSensorEnabled { get; set; } = false;
        public bool? DeviceControllerEnabled { get; set; } = false;
        public bool? BatteryEnabled { get; set; } = false;
        public bool? Debug { get; set; } = true;
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; } = false;
        public bool? FirmwareUpdate { get; set; }
        public string? FirmwareTargetVersion { get; set; }
        public bool? Enabled { get; set; } = false;
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }
    }

    /// The Web Edit form's ONLY binding target - deliberately carries just what EfRepository.DeviceUpdateAsync's own whitelist actually writes, so MacAddress/TenantID/IsGateway/GatewayProfile/ApiId/ApiKey/ConfigVersion have no property for an over-posted form value to land on, by construction rather than by remembering to filter them out downstream.
    public class DeviceEditForm
    {
        public int? IDDevice { get; set; }
        public int? DeviceRoleID { get; set; }
        public int? DeviceTypeServiceID { get; set; }
        public string? DeviceName { get; set; }
        public string? ServicePoint { get; set; }
        public string? ServicePublicKey { get; set; }
        public int? SleepSeconds { get; set; }
        public bool? SleepDeepEnabled { get; set; }
        public bool? DeviceSensorEnabled { get; set; }
        public bool? DeviceControllerEnabled { get; set; }
        public bool? BatteryEnabled { get; set; }
        public bool? Debug { get; set; }
        public bool? Enabled { get; set; }
    }

    public static class DeviceMappingExtensions
    {
        public static DeviceDto ToDto(this Device d) => new()
        {
            ConfigVersion = d.ConfigVersion,
            CommandVersion = d.CommandVersion,
            IDDevice = d.IDDevice,
            TenantID = d.TenantID,
            DeviceRoleID = d.DeviceRoleID,
            DeviceUnitID = d.DeviceUnitID,
            DeviceUnitZoneID = d.DeviceUnitZoneID,
            DeviceConfigSensorID = d.DeviceConfigSensorID,
            DeviceConfigControllerID = d.DeviceConfigControllerID,
            DeviceTypeServiceID = d.DeviceTypeServiceID,
            DeviceName = d.DeviceName,
            MacAddress = d.MacAddress,
            IsGateway = d.IsGateway,
            GatewayProfile = d.GatewayProfile,
            ServicePoint = d.ServicePoint,
            ServiceType = d.ServiceType,
            ServicePublicKey = d.ServicePublicKey,
            SleepSeconds = d.SleepSeconds,
            SleepDeepEnabled = d.SleepDeepEnabled,
            DeviceSensorEnabled = d.DeviceSensorEnabled,
            DeviceControllerEnabled = d.DeviceControllerEnabled,
            BatteryEnabled = d.BatteryEnabled,
            Debug = d.Debug,
            Reboot = d.Reboot,
            Reset = d.Reset,
            FirmwareUpdate = d.FirmwareUpdate,
            FirmwareTargetVersion = d.FirmwareTargetVersion,
            Enabled = d.Enabled,
            DateCreated = d.DateCreated,
            DateModified = d.DateModified,
        };

        /// The internal round-trip shape EfRepository/IRepository speak - ApiId/ApiKey are left unset here on purpose; DeviceUpdateAsync's own whitelist never reads them off the payload anyway, only off the freshly-loaded row.
        public static Device ToDevice(this DeviceDto dto) => new()
        {
            ConfigVersion = dto.ConfigVersion,
            CommandVersion = dto.CommandVersion,
            IDDevice = dto.IDDevice,
            TenantID = dto.TenantID,
            DeviceRoleID = dto.DeviceRoleID,
            DeviceUnitID = dto.DeviceUnitID,
            DeviceUnitZoneID = dto.DeviceUnitZoneID,
            DeviceConfigSensorID = dto.DeviceConfigSensorID,
            DeviceConfigControllerID = dto.DeviceConfigControllerID,
            DeviceTypeServiceID = dto.DeviceTypeServiceID,
            DeviceName = dto.DeviceName,
            MacAddress = dto.MacAddress,
            IsGateway = dto.IsGateway,
            GatewayProfile = dto.GatewayProfile,
            ServicePoint = dto.ServicePoint,
            ServiceType = dto.ServiceType,
            ServicePublicKey = dto.ServicePublicKey,
            SleepSeconds = dto.SleepSeconds,
            SleepDeepEnabled = dto.SleepDeepEnabled,
            DeviceSensorEnabled = dto.DeviceSensorEnabled,
            DeviceControllerEnabled = dto.DeviceControllerEnabled,
            BatteryEnabled = dto.BatteryEnabled,
            Debug = dto.Debug,
            Reboot = dto.Reboot,
            Reset = dto.Reset,
            FirmwareUpdate = dto.FirmwareUpdate,
            FirmwareTargetVersion = dto.FirmwareTargetVersion,
            Enabled = dto.Enabled,
            DateCreated = dto.DateCreated,
            DateModified = dto.DateModified,
        };

        /// Copies exactly the fields DeviceEditForm exposes onto an existing DeviceDto (fetched fresh from the API, never from client input) - every field the form can't carry (TenantID, MacAddress, IsGateway, ...) is left as whatever that fresh copy already had.
        public static void ApplyTo(this DeviceEditForm form, DeviceDto target)
        {
            target.DeviceRoleID = form.DeviceRoleID;
            target.DeviceTypeServiceID = form.DeviceTypeServiceID;
            target.DeviceName = form.DeviceName;
            target.ServicePoint = form.ServicePoint;
            target.ServicePublicKey = form.ServicePublicKey;
            target.SleepSeconds = form.SleepSeconds;
            target.SleepDeepEnabled = form.SleepDeepEnabled;
            target.DeviceSensorEnabled = form.DeviceSensorEnabled;
            target.DeviceControllerEnabled = form.DeviceControllerEnabled;
            target.BatteryEnabled = form.BatteryEnabled;
            target.Debug = form.Debug;
            target.Enabled = form.Enabled;
        }

        public static DeviceEditForm ToEditForm(this DeviceDto d) => new()
        {
            IDDevice = d.IDDevice,
            DeviceRoleID = d.DeviceRoleID,
            DeviceTypeServiceID = d.DeviceTypeServiceID,
            DeviceName = d.DeviceName,
            ServicePoint = d.ServicePoint,
            ServicePublicKey = d.ServicePublicKey,
            SleepSeconds = d.SleepSeconds,
            SleepDeepEnabled = d.SleepDeepEnabled,
            DeviceSensorEnabled = d.DeviceSensorEnabled,
            DeviceControllerEnabled = d.DeviceControllerEnabled,
            BatteryEnabled = d.BatteryEnabled,
            Debug = d.Debug,
            Enabled = d.Enabled,
        };
    }

    public class DeviceRegistration()
    {
        public string? MacAddress { get; set; }
        public string? Email { get; set; }
        // String, not int - the firmware always sends it as a string (char devicePin[8]).
        public string? DevicePin { get; set; }
        public string? ServicePoint { get; set; } = "api.agrumy.com";
        public int? ServiceType { get; set; } = 1;
        // Entered on the captive portal at first setup - only used as DeviceName when a new device has no Discovery-provisioned name already queued.
        public string? DisplayName { get; set; }

        // Agrumy.Gateway sends these on its own first registration so IsGateway/GatewayProfile come back set; null/false for ordinary firmware, and only consulted when the MacAddress is genuinely new. Honored only if GatewayRegistrationSecret matches the server's configured Gateway:RegistrationSecret - any other caller's IsGateway:true is silently dropped, registering an ordinary device instead.
        public bool IsGateway { get; set; }
        public GatewayProfile? GatewayProfile { get; set; }
        public string? GatewayRegistrationSecret { get; set; }
    }

    public class DeviceConfig()
    {
        public int? ConfigVersion { get; set; }
        public int? TenantID { get; set; }
        public int? deviceID { get; set; }
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public int? DeviceTypeServiceID { get; set; }

        public string? ApiId { get; set; }
        public string? ApiKey { get; set; }
        public string? ServicePoint { get; set; }
        public string? ServicePublicKey { get; set; }

        public int? SleepSeconds { get; set; } = 60;
        public bool? SleepDeep { get; set; } = false;

        // UTC offset (seconds, positive east) for ServerConfig.ScheduleTimeZone, computed fresh each sync so firmware needs no timezone database of its own; 0 when unconfigured.
        public int? UtcOffsetSeconds { get; set; }

        public bool? DeviceSensorEnabled { get; set; } = false;
        public bool? DeviceControllerEnabled { get; set; } = false;
        public bool? BatteryEnabled { get; set; } = false;
        public bool? Debug { get; set; }
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; }
        public bool? FirmwareUpdate { get; set; }
        // Populated by BuildDeviceConfigAsync from the newest deviceFirmware row only when FirmwareUpdate is true; null otherwise.
        public string? FirmwareVersion { get; set; }
        public string? FirmwareUrl { get; set; }
        // DeviceFirmware.Sha256 for the offered build; firmware verifies it against the streamed .bin (Update.abort() on mismatch). Null skips the check rather than failing closed.
        public string? FirmwareSha256 { get; set; }
        public bool? Enabled { get; set; }
        // Tenant-wide fail-closed switch (roadmap #230), from Tenant.EmergencyStopActive - ActuatorController forces every relay off ahead of any rule when set, independent of DeviceConfigController.RelayEnabled.
        public bool? EmergencyStop { get; set; }
        public DeviceConfigSensor? DeviceConfigSensor { get; set; }
        public DeviceConfigController? DeviceConfigController { get; set; }

        // Deliberately separate from ConfigVersion - a command must not force a full config re-apply, and GetConfig decides on whether a pending command exists, not by comparing this number.
        public int? CommandVersion { get; set; }
        // Null when there's nothing to do - present only for a real, unexpired Pending command (DeviceApiController.GetConfig/BuildDeviceConfigAsync).
        public PendingCommand? PendingCommand { get; set; }
    }

    /// Body of POST /api/Device/Config - poll doubles as heartbeat, so all fields are nullable to keep older firmware sending only ConfigVersion binding cleanly.
    public class DeviceConfigPoll()
    {
        public int? ConfigVersion { get; set; }
        public long? Uptime { get; set; }
        public int? Rssi { get; set; }
        public long? FreeHeap { get; set; }
        public string? FirmwareVersion { get; set; }
        // PlatformIO environment the image was built for (AGRUMY_BOARD flag) - selects the right catalog .bin for OTA; null from older firmware.
        public string? Board { get; set; }
        // Commercial board this image was built for (AGRUMY_KIT flag, e.g. "KC868-A6"), separate from Board; empty on generic chip-target, null from older firmware.
        public string? Kit { get; set; }
    }

    /// One device's row on the fleet dashboard; Battery comes from the latest sensorData row, not the heartbeat, since the firmware's own battery sensor is a stub.
    public class DeviceFleetStatus()
    {
        public int? IDDevice { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceName { get; set; }
        public bool? Enabled { get; set; }
        public int? SleepSeconds { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public long? UptimeSeconds { get; set; }
        public int? RssiDbm { get; set; }
        public long? FreeHeapBytes { get; set; }
        public string? FirmwareVersion { get; set; }
        // Catalog state for the Update button: LatestFirmwareVersion is the newest entry for this Board, FirmwareUpdateAvailable means it's newer than running, Pending/Target mirror Device.FirmwareUpdate/FirmwareTargetVersion.
        public string? Board { get; set; }
        public string? LatestFirmwareVersion { get; set; }
        public bool FirmwareUpdateAvailable { get; set; }
        public bool FirmwareUpdatePending { get; set; }
        public string? FirmwareTargetVersion { get; set; }
        public int? Battery { get; set; }
        public bool Online { get; set; }
        // Commercial board last reported in the heartbeat; empty = generic chip-target, null = never reported.
        public string? Kit { get; set; }
        // True when the device has real relay hardware - admin set DeviceRole to Sensor+Controller, or Kit maps to a deviceTypeKit board with relays; drives the Web UI's Controller tab.
        public bool ControllerCapable { get; set; }
        // Lets the Web layer filter one shared DeviceFleetGet() response down to a single zone's devices (DeviceUnitController.ZoneDetails) instead of a second endpoint.
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public string? DeviceUnitName { get; set; }
        public string? DeviceUnitZoneName { get; set; }
        /// Roadmap #343. Only the relay functions this device has ever reported a state for - empty for a sensor-only device or one whose firmware predates ControllerData.
        public IList<ControllerDataStatus>? RelayStates { get; set; }

        // 3 missed polls + fixed grace, not a bare SleepSeconds multiple - a cycle also costs work time (TLS/sensor reads), and grace floors the window when SleepSeconds=0.
        public const int OfflineMissedPolls = 3;
        public const int OfflineGraceSeconds = 90;

        /// Whether a device last seen at lastSeenAt (UTC) counts as online at utcNow, given its poll interval - static and time-injected so it's unit-testable without a repository.
        public static bool ComputeOnline(DateTime? lastSeenAt, int? sleepSeconds, DateTime utcNow)
        {
            if (lastSeenAt is not DateTime seen)
            {
                return false;
            }
            double windowSeconds = (sleepSeconds ?? 60) * (double)OfflineMissedPolls + OfflineGraceSeconds;
            return (utcNow - seen).TotalSeconds <= windowSeconds;
        }
    }

    public class DeviceUpdate()
    {
        public DeviceDto? Device {  get; set; }
        public DeviceConfigSensor? Sensor { get; set; }
        public DeviceConfigController? Controller { get; set; }
    }

    public class DeviceAuthentication()
    {
        public string? apiAuth { get; set; }

    }


    public class DeviceConfigSensor()
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceConfigSensor { get; set; }
        // 0/null=Disabled, 1009=MAX17048 (I2C fuel gauge), 2001=Analog VoltageDivider - same deviceTypeSensor dropdown as every other Sensor* field.
        public int? SensorBattery { get; set; }
        // VoltageDivider calibration (sensorBattery=2001 only), actual wired resistor ohms: V_battery = V_measured * (R1+R2)/R2; ignored by MAX17048.
        public double? BatteryDividerR1 { get; set; }
        public double? BatteryDividerR2 { get; set; }
        public int? SensorTemp { get; set; }
        public int? SensorTempSoil { get; set; }
        public int? SensorHumid { get; set; }
        public int? SensorMoist { get; set; }
        public int? SensorLight { get; set; }
        public int? SensorCo2 { get; set; }
        public int? SensorTvoc { get; set; }
        public int? SensorBarometer { get; set; }
        public int? SensorPH { get; set; }
        public int? SensorRainLevel { get; set; }
        public int? SensorWaterLevel { get; set; }
        public int? SensorWind { get; set; }

    }

    /// One physically-wired relay slot and the RelayFunction assigned to it - Slot is 1-based, matching AgrumyFirmware's ConfigPin.RELAY_PINS[Slot-1]. A slot with no row is unassigned/disabled; there is no fixed count baked into this shape, unlike the old fixed Relay1..Relay8 columns.
    public class DeviceRelaySlot
    {
        public int Slot { get; set; }
        public int RelayFunction { get; set; }
    }

    /// Bumping this alone (plus a matching AgrumyFirmware MAX_RELAY_SLOTS bump for boards that need more) is now the entire "support more relay slots" story - no schema/wire-format change needed.
    public static class RelaySlotLimits
    {
        public const int MaxSlots = 8;
    }

    /// What's left of the per-device model after thresholds/schedule/safety-limits moved to the zone (DeviceUnitZone/DeviceUnitZoneRule) - just the relay-pin mapping; Rules/WaterPump* below are populated from the assigned zone by BuildDeviceConfigAsync, not from this row.
    public class DeviceConfigController()
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceConfigController { get; set; }

        // The assigned zone's rules for whichever RelayFunction(s) Relays wires up; empty (all off) when the device has no zone.
        public IList<DeviceUnitZoneRule> Rules { get; set; } = [];

        // Copied from the assigned zone's own fields - see DeviceUnitZone's remarks for why these are not Rules.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // Final AND-NOT veto over WaterPump (BuildDeviceConfigAsync, from SkipWaterPumpWhenRainPredicted && WeatherRainPredicted) - not a Rule, since OR-combined rules can only add a run reason, never suppress one.
        public bool SkipWaterPumpForRain { get; set; }

        // Physical/hardware, stays per-device.
        public bool? RelayEnabled { get; set; }
        // One entry per assigned slot only - an unlisted slot is unassigned/disabled.
        public IList<DeviceRelaySlot> Relays { get; set; } = [];
    }

    /// One wall-clock window in one of DeviceConfigController's per-function schedule lists - no RelayFunction/Enabled fields, since list membership itself means both.
    public class DeviceScheduleSlot
    {
        /// 7-bit mask, bit 0 = Sunday .. bit 6 = Saturday (C's tm_wday convention).
        public int DaysOfWeek { get; set; }
        /// Seconds since local midnight, 0-86399.
        public int Start { get; set; }
        /// Seconds; Start + Duration must not exceed 86400 (no crossing local midnight).
        public int Duration { get; set; }
    }

    public class DeviceRole()
    {
        public int? IDDeviceRole { get; set; }
        public string? DeviceRoleName { get; set; }
        public bool? SensorEnabled { get; set; } = false;
        public bool? ControllerEnabled { get; set; } = false;
    }

    public class DeviceTypeService()
    {
        public int? IDDeviceTypeService { get; set; }
        public string? ServiceType { get; set; }
    }

    public class DeviceTypeRelay()
    {
        public int? IDDeviceTypeRelay { get; set; }
        public string? RelayName { get; set; }
    }

    public class DeviceTypeSensor()
    {
        public int? IDDeviceTypeSensor { get; set; }
        public string? SensorName { get; set; }
        public string? SensorDescription { get; set; }

        public int? Battery { get; set; }
        public int? Temperature { get; set; }
        public int? TemperatureSoil { get; set; }
        public int? Humidity { get; set; }
        public int? Moisture { get; set; }
        public int? Light { get; set; }
        public int? Co2 { get; set; }
        public int? Tvoc { get; set; }
        public int? Barometer { get; set; }
        public int? WaterPH { get; set; }
        public int? WaterTankLevel { get; set; }
        public int? RainLevel { get; set; }
        public int? Wind { get; set; }

    }

    public class DeviceCache()
    {
        public string? apiAuth { get; set; }
    }
}
