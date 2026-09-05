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

        public int? DeviceTypeID { get; set; } = 0;
        [HiddenInput(DisplayValue = true)]
        public int? DeviceUnitID { get; set; } = 0;
        [HiddenInput(DisplayValue = true)]
        public int? DeviceUnitZoneID { get; set; } = 0;
        [HiddenInput(DisplayValue = true)]
        public int? DeviceConfigSensorID { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? DeviceConfigControllerID { get; set; }

        public int? DeviceTypeServiceID { get; set; } = 0;

        public string? DeviceName { get; set; }
        [HiddenInput(DisplayValue = true)]
        public string? MacAddress { get; set; }
        // True only for an Agrumy.Relay instance, flagged so RelayApiController.Batch can tell a relay's own credential from an ordinary device's.
        [HiddenInput(DisplayValue = true)]
        public bool IsRelay { get; set; }
        // Null for non-relay devices; set once at registration (DeviceRegistration.RelayProfile), not editable afterward since Profile A/B imply different physical setups.
        [HiddenInput(DisplayValue = true)]
        public RelayProfile? RelayProfile { get; set; }
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


    }

    public class DeviceRegistration()
    {
        public string? MacAddress { get; set; }
        public string? Email { get; set; }
        // String, not int - the firmware always sends it as a string (char devicePin[8]).
        public string? DevicePin { get; set; }
        public string? ServicePoint { get; set; } = "api.agrumy.com";
        public int? ServiceType { get; set; } = 1;

        // Agrumy.Relay sends these on its own first registration so IsRelay/RelayProfile come back set; null/false for ordinary firmware, and only consulted when the MacAddress is genuinely new. Honored only if RelayRegistrationSecret matches the server's configured Relay:RegistrationSecret - any other caller's IsRelay:true is silently dropped, registering an ordinary device instead.
        public bool IsRelay { get; set; }
        public RelayProfile? RelayProfile { get; set; }
        public string? RelayRegistrationSecret { get; set; }
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
        // True when the device has real relay hardware - admin set DeviceType to Sensor+Controller, or Kit maps to a deviceTypeKit board with relays; drives the Web UI's Controller tab.
        public bool ControllerCapable { get; set; }
        // Lets the Web layer filter one shared DeviceFleetGet() response down to a single zone's devices (DeviceUnitController.ZoneDetails) instead of a second endpoint.
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public string? DeviceUnitName { get; set; }
        public string? DeviceUnitZoneName { get; set; }

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
        public Device? Device {  get; set; }
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

    /// What's left of the per-device model after thresholds/schedule/safety-limits moved to the zone (DeviceUnitZone/DeviceUnitZoneRule) - just the relay-pin mapping; Rules/WaterPump* below are populated from the assigned zone by BuildDeviceConfigAsync, not from this row.
    public class DeviceConfigController()
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceConfigController { get; set; }

        // The assigned zone's rules for whichever RelayFunction(s) Relay1-8 wires up; empty (all off) when the device has no zone.
        public IList<DeviceUnitZoneRule> Rules { get; set; } = [];

        // Copied from the assigned zone's own fields - see DeviceUnitZone's remarks for why these are not Rules.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // Final AND-NOT veto over WaterPump (BuildDeviceConfigAsync, from SkipWaterPumpWhenRainPredicted && WeatherRainPredicted) - not a Rule, since OR-combined rules can only add a run reason, never suppress one.
        public bool SkipWaterPumpForRain { get; set; }

        // Physical/hardware, stays per-device.
        public bool? RelayEnabled { get; set; }
        public int? Relay1 { get; set; }
        public int? Relay2 { get; set; }
        public int? Relay3 { get; set; }
        public int? Relay4 { get; set; }
        public int? Relay5 { get; set; }
        public int? Relay6 { get; set; }
        public int? Relay7 { get; set; }
        public int? Relay8 { get; set; }

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

    public class DeviceType()
    {
        public int? IDDeviceType { get; set; }
        public string? DeviceTypeName { get; set; }
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
