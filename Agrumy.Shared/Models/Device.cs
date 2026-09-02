using Microsoft.AspNetCore.Mvc;

namespace api.Models
{
    public class Device
    {
        public int? ConfigVersion { get; set; } = 1;

        [HiddenInput(DisplayValue = true)]
        public int? IDDevice { get; set; }
        // Roadmap #112: non-nullable - TenantID=0 is a real, meaningful default tenant (user
        // confirmed), not a "no tenant" sentinel, so a nullable int let an impossible third state
        // leak into every consumer as a `?? 0` fallback (see #96/#100/#106/#102/#108/#111).
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
        public string? ApiId { get; set; }
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
        public bool? Enabled { get; set; } = false;


        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }


    }

    public class DeviceRegistration()
    {
        public string? MacAddress { get; set; }
        public string? Email { get; set; }
        // Roadmap #70: string, not int - the firmware always sent it as a string (char devicePin[8]),
        // the int? binding only worked via Web-defaults number-from-string parsing.
        public string? DevicePin { get; set; }
        public string? ServicePoint { get; set; } = "api.agrumy.com";
        public int? ServiceType { get; set; } = 1;

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

        // Roadmap #39: current UTC offset (seconds, positive east of UTC) for
        // ServerConfig.ScheduleTimeZone, computed fresh on every config sync
        // (DeviceApiController.BuildDeviceConfigAsync via TimeZoneHelper.GetUtcOffsetSeconds) so a
        // DST transition reaches every device within one poll cycle without the firmware needing a
        // timezone database of its own. 0 (UTC) when no ScheduleTimeZone is configured yet.
        public int? UtcOffsetSeconds { get; set; }

        public bool? DeviceSensorEnabled { get; set; } = false;
        public bool? DeviceControllerEnabled { get; set; } = false;
        public bool? BatteryEnabled { get; set; } = false;
        public bool? Debug { get; set; }
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; }
        public bool? FirmwareUpdate { get; set; }
        // Roadmap #3 (OTA): populated by BuildDeviceConfigAsync from the newest deviceFirmware
        // row for the device's type, but only when FirmwareUpdate == true. Null otherwise.
        public string? FirmwareVersion { get; set; }
        public string? FirmwareUrl { get; set; }
        public bool? Enabled { get; set; }
        public DeviceConfigSensor? DeviceConfigSensor { get; set; }
        public DeviceConfigController? DeviceConfigController { get; set; }

    }

    /// <summary>Body of POST /api/Device/Config (roadmap #7): the poll doubles as the heartbeat, so
    /// besides ConfigVersion the firmware reports its live diagnostics every cycle. All fields are
    /// nullable so a pre-#7 firmware that sends only ConfigVersion still binds cleanly.</summary>
    public class DeviceConfigPoll()
    {
        public int? ConfigVersion { get; set; }
        public long? Uptime { get; set; }
        public int? Rssi { get; set; }
        public long? FreeHeap { get; set; }
        public string? FirmwareVersion { get; set; }
    }

    /// <summary>One device's row on the fleet dashboard (roadmap #8). Battery comes from the latest
    /// sensorData row, not the heartbeat - the firmware battery sensor is a stub until roadmap #12,
    /// and telemetry is where a real reading will land anyway.</summary>
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
        public int? Battery { get; set; }
        public bool Online { get; set; }
        // Roadmap #116: lets the Web layer filter one shared DeviceFleetGet() response down to a
        // single zone's devices for DeviceUnitController.ZoneDetails, instead of a second endpoint.
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }

        // 3 missed polls + fixed grace, not a bare multiple of SleepSeconds: a cycle is sleep time
        // PLUS work time (TLS handshakes, sensor reads), and a single dropped poll on a flaky WiFi
        // link should not flip a device to offline. Grace also floors the window for SleepSeconds=0.
        public const int OfflineMissedPolls = 3;
        public const int OfflineGraceSeconds = 90;

        /// <summary>Whether a device that last polled at <paramref name="lastSeenAt"/> (UTC) should
        /// count as online at <paramref name="utcNow"/>, given how often it is configured to poll.
        /// Static and time-injected so the threshold rule is unit-testable without a repository.</summary>
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
        public int? SensorBattery { get; set; }
        public int? SensorTemp { get; set; }//
        public int? SensorTempSoil { get; set; }
        public int? SensorHumid { get; set; }
        public int? SensorMoist { get; set; }//
        public int? SensorLight { get; set; } // promijeniti u illumination 
        public int? SensorCo2 { get; set; }//
        public int? SensorTvoc { get; set; }
        public int? SensorBarometer { get; set; }
        public int? SensorPH { get; set; }
        public int? SensorRainLevel { get; set; }
        public int? SensorWaterLevel { get; set; }
        public int? SensorWind { get; set; }

    }

    public class DeviceConfigController()
    {
        // Sensor values
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceConfigController { get; set; }
        public double? TempLow { get; set; }
        public double? TempHigh { get; set; }
        public double? HumidLow { get; set; }
        public double? HumidHigh { get; set; }
        public double? MoistLow { get; set; }
        public double? MoistHigh { get; set; }
        public double? LightLow { get; set; }
        public double? LightHigh { get; set; }
        public double? WaterLow { get; set; }
        public double? WaterHigh { get; set; }

        // Hysteresis (dead zone) margins for the threshold-based relay logic - prevents
        // chattering when a sensor value sits right at its threshold. Seeded from ServerConfig's
        // matching fields when the device is created; editable per device from here on.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        // Manual timming
        public bool? VentilationIntervalEnabled {  get; set; }
        public int? VentilationInterval {  get; set; }
        public int? VentilationIntervalLength { get; set; }

        public bool? LightIntervalEnabled { get; set; }
        public int? LightInterval { get; set; }
        public int? LightIntervalLength { get; set; }

        public bool? HeatingIntervalEnabled { get; set; }
        public int? HeatingInterval { get; set; }
        public int? HeatingIntervalLength { get; set; }

        public bool? WaterPumpIntervalEnabled { get; set; }
        public int? WaterPumpInterval { get; set; }
        public int? WaterPumpIntervalLength { get; set; }

        // Roadmap #39/#115: a third relay-control mode alongside threshold (dead-zone) and
        // interval (duty-cycle) above - "be on during any of these wall-clock windows on these
        // days", independent of any sensor reading. Each function gets zero or more windows,
        // OR'd together by the firmware (AgrumyDevice's RelayLogic::computeAnyScheduleState) -
        // zero windows means that function never turns on in schedule mode, no separate "enabled"
        // flag needed (confirmed design, #115). DaysOfWeek is a 7-bit mask matching C's tm_wday
        // convention (bit 0 = Sunday .. bit 6 = Saturday - see AgrumyDevice's
        // ActuatorController::scheduleRelayFunction), so the firmware needs zero day-numbering
        // translation. Start/Duration are seconds since LOCAL midnight (not UTC) - "local"
        // resolved via ServerConfig.ScheduleTimeZone and delivered to the device as a plain UTC
        // offset (DeviceConfig.UtcOffsetSeconds) rather than an IANA id, so the firmware needs no
        // timezone database, just integer math refreshed every config poll. v1 deliberately does
        // not support a window crossing local midnight (Start + Duration must stay within the
        // same calendar day) - see DeviceApiController's validation.
        public List<DeviceScheduleSlot> VentilationSchedule { get; set; } = [];
        public List<DeviceScheduleSlot> LightSchedule { get; set; } = [];
        public List<DeviceScheduleSlot> HeatingSchedule { get; set; } = [];
        public List<DeviceScheduleSlot> WaterPumpSchedule { get; set; } = [];

        // Relay
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

    /// <summary>Roadmap #115: one wall-clock window within one of DeviceConfigController's four
    /// per-function schedule lists. No RelayFunction/Enabled fields here - which function it
    /// belongs to is which list it's in, and its presence in the list IS "enabled".</summary>
    public class DeviceScheduleSlot
    {
        /// <summary>7-bit mask, bit 0 = Sunday .. bit 6 = Saturday (C's tm_wday convention).</summary>
        public int DaysOfWeek { get; set; }
        /// <summary>Seconds since local midnight, 0-86399.</summary>
        public int Start { get; set; }
        /// <summary>Seconds; Start + Duration must not exceed 86400 (no crossing local midnight).</summary>
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

    // Roadmap #106: ConfigVersion used to live here too, but GetConfig now compares against the
    // device row it already reads for the #7 diagnostics upsert - a second, independently-staled
    // copy in the session cache was pure risk (root cause of #100 and the multi-instance drift
    // #72 raised) with no benefit once that DB read became mandatory on every poll.
    public class DeviceCache()
    {
        public string? apiAuth { get; set; }
    }
}