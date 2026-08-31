using Microsoft.AspNetCore.Mvc;

namespace api.Models
{
    public class Device
    {
        public int? ConfigVersion { get; set; } = 1;

        [HiddenInput(DisplayValue = true)]
        public int? IDDevice { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? TenantID { get; set; } = 0;

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
        public int? VentilationIntervalLenght { get; set; }

        public bool? LightIntervalEnabled { get; set; }
        public int? LightInterval { get; set; }
        public int? LightIntervalLenght { get; set; }

        public bool? HeatingIntervalEnabled { get; set; }
        public int? HeatingInterval { get; set; }
        public int? HeatingIntervalLenght { get; set; }

        public bool? WaterPumpIntervalEnabled { get; set; }
        public int? WaterPumpInterval { get; set; }
        public int? WaterPumpIntervalLenght { get; set; }

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
        public int? ConfigVersion { get; set; }
        public string? apiAuth { get; set; }        
        
    }
}