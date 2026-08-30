namespace api.Dal.Entities
{
    public class DeviceUnitZoneRow
    {
        public int IDDeviceUnitZone { get; set; }
        public string? DeviceUnitZoneName { get; set; }
    }

    public class DeviceUnitRow
    {
        public int IDDeviceUnit { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public string? DeviceUnitName { get; set; }
        public bool? ZoneEnabled { get; set; }
    }

    public class DeviceTypeRow
    {
        public int IDDeviceType { get; set; }
        public string? DeviceTypeName { get; set; }
        public bool? SensorEnabled { get; set; }
        public bool? ControllerEnabled { get; set; }
    }

    public class DeviceTypeServiceRow
    {
        public int IDDeviceTypeService { get; set; }
        public string? ServiceType { get; set; }
    }

    public class DeviceTypeRelayRow
    {
        public int IDDeviceTypeRelay { get; set; }
        public string? RelayName { get; set; }
    }

    public class DeviceTypeSensorRow
    {
        public int IDDeviceTypeSensor { get; set; }
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

    public class DeviceConfigControllerRow
    {
        public int IDDeviceConfigController { get; set; }
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

        // Hysteresis (dead zone) margins - see api.Models.DeviceConfigController.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        // These columns exist in every deployed deviceConfigController table (pre-dating this
        // codebase's EF Core migration) but were never declared on this entity, so EF silently
        // never read or wrote them - the Web admin's Interval tab always saved into the void.
        // Default member initializers match the DB column defaults (all 0/false).
        public bool? VentilationIntervalEnabled { get; set; } = false;
        public int? VentilationInterval { get; set; } = 0;
        public int? VentilationIntervalLenght { get; set; } = 0;
        public bool? LightIntervalEnabled { get; set; } = false;
        public int? LightInterval { get; set; } = 0;
        public int? LightIntervalLenght { get; set; } = 0;
        public bool? HeatingIntervalEnabled { get; set; } = false;
        public int? HeatingInterval { get; set; } = 0;
        public int? HeatingIntervalLenght { get; set; } = 0;
        public bool? WaterPumpIntervalEnabled { get; set; } = false;
        public int? WaterPumpInterval { get; set; } = 0;
        public int? WaterPumpIntervalLenght { get; set; } = 0;

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

    public class DeviceConfigSensorRow
    {
        public int IDDeviceConfigSensor { get; set; }
        public int? SensorBattery { get; set; }
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

    public class DeviceRow
    {
        public int IDDevice { get; set; }
        public int? TenantID { get; set; }
        public int? DeviceTypeID { get; set; }
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public int? DeviceConfigSensorID { get; set; }
        public int? DeviceConfigControllerID { get; set; }
        public int? DeviceTypeServiceID { get; set; }
        public string? DeviceName { get; set; }
        public string? MacAddress { get; set; }
        public string ApiId { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string? ServicePoint { get; set; }
        public string? ServicePublicKey { get; set; }
        public int? SleepSeconds { get; set; }
        public bool? SleepDeepEnabled { get; set; }
        public bool? DeviceSensorEnabled { get; set; }
        public bool? DeviceControllerEnabled { get; set; }
        public bool? BatteryEnabled { get; set; }
        public bool? Enabled { get; set; }
        public bool? Debug { get; set; }
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; }
        public bool? FirmwareUpdate { get; set; }
        public int? ConfigVersion { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }
    }

    public class DeviceFirmwareRow
    {
        public int IDDeviceFirmware { get; set; }
        public int? DeviceTypeID { get; set; }
        public string? Version { get; set; }
        public string? Url { get; set; }
        public DateTime? DateAdded { get; set; }
    }
}
