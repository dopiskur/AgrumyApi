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
        // Non-nullable - TenantID=0 is a real, meaningful default tenant, not a "no tenant"
        // sentinel, so a nullable int would let an impossible third state leak into every consumer.
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
        // Null = "latest for the board" when FirmwareUpdate is set; a specific catalog version
        // pins a rollback/downgrade. Both cleared by DeviceApiController.GetConfig once the
        // heartbeat reports that exact version running.
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

        // Current UTC offset (seconds, positive east of UTC) for ServerConfig.ScheduleTimeZone,
        // computed fresh on every config sync so the firmware needs no timezone database of its
        // own. 0 (UTC) when no ScheduleTimeZone is configured yet.
        public int? UtcOffsetSeconds { get; set; }

        public bool? DeviceSensorEnabled { get; set; } = false;
        public bool? DeviceControllerEnabled { get; set; } = false;
        public bool? BatteryEnabled { get; set; } = false;
        public bool? Debug { get; set; }
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; }
        public bool? FirmwareUpdate { get; set; }
        // Populated by BuildDeviceConfigAsync from the newest deviceFirmware row for the device's
        // type, but only when FirmwareUpdate == true. Null otherwise.
        public string? FirmwareVersion { get; set; }
        public string? FirmwareUrl { get; set; }
        // DeviceFirmware.Sha256 for the offered build, when the catalog has one - firmware verifies
        // it against the streamed .bin, Update.abort() on mismatch. Null whenever the source had no
        // manifest hash - the check is skipped rather than failing closed; it is not proof the .bin is bad.
        public string? FirmwareSha256 { get; set; }
        public bool? Enabled { get; set; }
        public DeviceConfigSensor? DeviceConfigSensor { get; set; }
        public DeviceConfigController? DeviceConfigController { get; set; }

        // Deliberately separate from ConfigVersion - issuing a command must not force a full
        // config re-apply on the firmware side, and a config change must not touch the command
        // queue. The server's decision to send a non-empty response is driven by whether a pending
        // command actually exists (GetConfig), not by comparing this number.
        public int? CommandVersion { get; set; }
        // Null when there is nothing to do - present only when a real, unexpired Pending command
        // is waiting (DeviceApiController.GetConfig/BuildDeviceConfigAsync).
        public PendingCommand? PendingCommand { get; set; }
    }

    /// <summary>Body of POST /api/Device/Config: the poll doubles as the heartbeat, so besides
    /// ConfigVersion the firmware reports its live diagnostics every cycle. All fields are
    /// nullable so an older firmware that sends only ConfigVersion still binds cleanly.</summary>
    public class DeviceConfigPoll()
    {
        public int? ConfigVersion { get; set; }
        public long? Uptime { get; set; }
        public int? Rssi { get; set; }
        public long? FreeHeap { get; set; }
        public string? FirmwareVersion { get; set; }
        // PlatformIO environment the running image was built for (AGRUMY_BOARD build flag) -
        // selects the right catalog .bin for OTA. Null from an older firmware.
        public string? Board { get; set; }
        // Which commercial physical board this image was built for (AGRUMY_KIT build flag, e.g.
        // "KC868-A6") - separate from Board, which only selects the OTA binary. Empty string on a
        // generic chip-target environment, null from an older firmware.
        public string? Kit { get; set; }
    }

    /// <summary>One device's row on the fleet dashboard. Battery comes from the latest sensorData
    /// row, not the heartbeat - the firmware battery sensor is a stub, and telemetry is where a
    /// real reading will land.</summary>
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
        // Catalog state for the "Update" button - LatestFirmwareVersion is the newest catalog
        // entry for this device's Board, FirmwareUpdateAvailable is "that is newer than what's
        // running", and the Pending/Target pair mirrors Device.FirmwareUpdate/FirmwareTargetVersion.
        public string? Board { get; set; }
        public string? LatestFirmwareVersion { get; set; }
        public bool FirmwareUpdateAvailable { get; set; }
        public bool FirmwareUpdatePending { get; set; }
        public string? FirmwareTargetVersion { get; set; }
        public int? Battery { get; set; }
        public bool Online { get; set; }
        // Which commercial physical board last reported in the heartbeat (empty = generic
        // chip-target environment, null = never reported one).
        public string? Kit { get; set; }
        // True when this device is treated as having real, wired relay hardware - either the admin
        // already set DeviceType to Sensor+Controller, OR Kit names a board the deviceTypeKit
        // lookup knows has relays. Drives whether the Web UI shows the Controller config tab.
        public bool ControllerCapable { get; set; }
        // Lets the Web layer filter one shared DeviceFleetGet() response down to a single zone's
        // devices for DeviceUnitController.ZoneDetails, instead of a second endpoint.
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
        // 0/null=Disabled(None), 1009=MAX17048 (I2C fuel gauge, coulomb counting), 2001=Analog
        // voltage (VoltageDivider) - same deviceTypeSensor-backed dropdown as every other Sensor* field here.
        public int? SensorBattery { get; set; }
        // VoltageDivider calibration (sensorBattery=2001 only) - the ACTUAL resistors the user
        // wired, in ohms, not an abstract preset ratio: V_battery = V_measured * (R1+R2)/R2.
        // Ignored by MAX17048, which reports state-of-charge directly.
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

    /// <summary>What's LEFT of this per-DEVICE model after thresholds/schedule/safety-limits all
    /// moved to the ZONE (see DeviceUnitZone/DeviceUnitZoneRule) - just the relay-pin mapping,
    /// which stays device-side because it is a physical/hardware fact about THIS controller.
    /// Rules/WaterPumpMaxRunSeconds/WaterPumpCooldownSeconds below are populated from the device's
    /// ASSIGNED ZONE, not from this row, when DeviceApiController builds a config-poll response -
    /// see BuildDeviceConfigAsync.</summary>
    public class DeviceConfigController()
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceConfigController { get; set; }

        // The zone's rules for whichever RelayFunction(s) this device's Relay1-8 mapping below
        // actually wires up - empty when the device has no assigned zone, so every relay function
        // simply stays off.
        public IList<DeviceUnitZoneRule> Rules { get; set; } = [];

        // Copied from the assigned zone's own fields - see DeviceUnitZone's remarks for why these are not Rules.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // Final AND-NOT veto over WaterPump, computed server-side (BuildDeviceConfigAsync) from
        // DeviceUnitZone.SkipWaterPumpWhenRainPredicted && ServerConfig.WeatherRainPredicted - not
        // a Rule, since OR-combining rules could only ever ADD a reason to run, never suppress one.
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

    /// <summary>One wall-clock window within one of DeviceConfigController's four per-function
    /// schedule lists. No RelayFunction/Enabled fields here - which function it belongs to is
    /// which list it's in, and its presence in the list IS "enabled".</summary>
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

    public class DeviceCache()
    {
        public string? apiAuth { get; set; }
    }
}