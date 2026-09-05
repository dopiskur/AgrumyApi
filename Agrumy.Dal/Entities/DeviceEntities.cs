namespace api.Dal.Entities
{
    /// TenantID null means the shared global sentinel row (IDDeviceUnit=0 "Default", see EfRepository.SeedDeviceUnitSentinelsAsync), not a real per-tenant Unit.
    public class DeviceUnitRow
    {
        public int IDDeviceUnit { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceUnitName { get; set; }
        public bool? ZoneEnabled { get; set; }
    }

    /// DeviceUnitID is the real "Unit contains many Zones" FK (see db/migrations/2026-09-02-deviceunit-zone-containment.sql) - TenantID null means the shared global sentinel row (IDDeviceUnitZone=0 "Disabled"), same convention as DeviceUnitRow.
    public class DeviceUnitZoneRow
    {
        public int IDDeviceUnitZone { get; set; }
        public int? TenantID { get; set; }
        public int DeviceUnitID { get; set; }
        public string? DeviceUnitZoneName { get; set; }

        // See api.Models.DeviceUnitZone's own copy of these for the full explanation.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // See api.Models.DeviceUnitZone.SkipWaterPumpWhenRainPredicted.
        public bool SkipWaterPumpWhenRainPredicted { get; set; }
    }

    /// See api.Models.DeviceUnitZoneRule - ConditionConfig is stored as plain JSON text, (de)serialized at the application layer, not a native JSON column type.
    public class DeviceUnitZoneRuleRow
    {
        public int IDDeviceUnitZoneRule { get; set; }
        public int DeviceUnitZoneID { get; set; }
        public int RelayFunction { get; set; }
        public int ConditionType { get; set; }
        public string ConditionConfig { get; set; } = "{}";
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

        // Default member initializers match the DB column defaults (all 0/false).
        public bool? VentilationIntervalEnabled { get; set; } = false;
        public int? VentilationInterval { get; set; } = 0;
        public int? VentilationIntervalLength { get; set; } = 0;
        public bool? LightIntervalEnabled { get; set; } = false;
        public int? LightInterval { get; set; } = 0;
        public int? LightIntervalLength { get; set; } = 0;
        public bool? HeatingIntervalEnabled { get; set; } = false;
        public int? HeatingInterval { get; set; } = 0;
        public int? HeatingIntervalLength { get; set; } = 0;
        public bool? WaterPumpIntervalEnabled { get; set; } = false;
        public int? WaterPumpInterval { get; set; } = 0;
        public int? WaterPumpIntervalLength { get; set; } = 0;

        // See api.Models.DeviceConfigController.WaterPumpMaxRunSeconds/WaterPumpCooldownSeconds.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        public bool? RelayEnabled { get; set; }
    }

    /// One physically-wired relay slot assigned to a RelayFunction (roadmap #309) - only assigned slots get a row, replacing the fixed Relay1..Relay8 columns that used to live on DeviceConfigControllerRow.
    public class DeviceConfigControllerRelayRow
    {
        public int IDDeviceConfigController { get; set; }
        public int Slot { get; set; }
        public int RelayFunction { get; set; }
    }

    /// One wall-clock window for one relay function (RelayFunction = deviceTypeRelay's seed IDs, same convention as ActuatorController::RelayFunctionType) - a row's mere presence means it is active, there is no separate Enabled column.
    public class DeviceScheduleSlotRow
    {
        public int IDDeviceScheduleSlot { get; set; }
        public int DeviceConfigControllerID { get; set; }
        public int RelayFunction { get; set; }
        public int DaysOfWeek { get; set; }
        public int Start { get; set; }
        public int Duration { get; set; }
    }

    public class DeviceConfigSensorRow
    {
        public int IDDeviceConfigSensor { get; set; }
        public int? SensorBattery { get; set; }
        // See api.Models.DeviceConfigSensor.BatteryDividerR1/R2.
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

    public class DeviceRow
    {
        public int IDDevice { get; set; }
        // Non-nullable, matching the DB column (NOT NULL DEFAULT 0).
        public int TenantID { get; set; }
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
        // See api.Models.Device.FirmwareTargetVersion.
        public string? FirmwareTargetVersion { get; set; }
        public int? ConfigVersion { get; set; }
        // See api.Models.DeviceConfig.CommandVersion for the full story.
        public int CommandVersion { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        // See api.Models.Device's own copies of these for the full explanation.
        public bool IsGateway { get; set; }
        public int? GatewayProfile { get; set; }

        // See api.Models.Device.LastFullConfigSentAt for the full explanation.
        public DateTime? LastFullConfigSentAt { get; set; }
    }

    /// One LoRaWAN end-device's DevEUI mapped to the Agrumy device (ApiId/ApiKey) a LoRaGateway acts on behalf of for that DevEUI's uplinks.
    public class GatewayDeviceMappingRow
    {
        public int IDGatewayDeviceMapping { get; set; }
        public int IDGatewayDevice { get; set; }
        public string DevEUI { get; set; } = "";
        public int IDDevice { get; set; }
        public DateTime? DateCreated { get; set; }
    }

    /// One scanning device's sighting of one nearby Agrumy_ AP during a discovery scan - raw reports, not yet deduplicated/best-picked (that lives in the repository query layer).
    public class DeviceDiscoveryReportRow
    {
        public int IDReport { get; set; }
        public int ScanningDeviceID { get; set; }
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
        public DateTime DateReported { get; set; }
    }

    /// One discrete, one-shot device action - see api.Models.CommandStatus for why Acknowledged is a real, persisted state.
    public class DeviceCommandRow
    {
        public int IDDeviceCommand { get; set; }
        public int DeviceID { get; set; }
        public int ActionType { get; set; }
        public int Status { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        // Mirrors ActionType while active, NULL once terminal - backs the unique (DeviceID, ActiveKey) index that makes IssueCommandAsync's dedup a real DB constraint.
        public int? ActiveKey { get; set; }
        public string? Payload { get; set; }
    }

    /// One row per device, upserted on every config poll (the poll itself is the heartbeat) - keyed by DeviceID (1:1 with device), not an identity column, so the upsert is a plain read-or-insert.
    public class DeviceDiagnosticRow
    {
        public int DeviceID { get; set; }
        public int? TenantID { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public long? UptimeSeconds { get; set; }
        public int? RssiDbm { get; set; }
        public long? FreeHeapBytes { get; set; }
        // When OfflineAlertBackgroundService last notified admins about this device's CURRENT
        // offline streak - null means either never offline, or back online since the last alert.
        // One notification per streak, not one per tick, without a separate dedup table.
        public DateTime? OfflineNotifiedAt { get; set; }
        // Same dedup-by-streak rule as OfflineNotifiedAt above, but for LowBatteryAlertEvaluator -
        // null means either never low, or recovered above ServerConfig.BatteryLowThreshold +
        // BatteryLowHysteresis since the last alert.
        public DateTime? LowBatteryNotifiedAt { get; set; }
        public string? FirmwareVersion { get; set; }
        // See api.Models.DeviceConfigPoll.Board.
        public string? Board { get; set; }
        // See api.Models.DeviceConfigPoll.Kit.
        public string? Kit { get; set; }
    }

    /// Catalog of recognized commercial kits and whether each has real wired relay hardware - Kit itself is the key (a build-flag string, e.g. "KC868-A6"), not an auto-increment id.
    public class DeviceTypeKitRow
    {
        public string Kit { get; set; } = "";
        public bool ControllerCapable { get; set; }
    }

    // Board/Source/FileName/SizeBytes/Sha256/PublishedAt - see api.Models.DeviceFirmware for what
    // each means; DeviceTypeID is the legacy key.
    public class DeviceFirmwareRow
    {
        public int IDDeviceFirmware { get; set; }
        public int? DeviceTypeID { get; set; }
        public string? Board { get; set; }
        public string? Version { get; set; }
        public string? Url { get; set; }
        public int Source { get; set; }
        public string? FileName { get; set; }
        public long? SizeBytes { get; set; }
        public string? Sha256 { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? DateAdded { get; set; }

        // See api.Models.DeviceFirmware's own copy of these for the full explanation.
        public string? FullImageFileName { get; set; }
        public string? FullImageUrl { get; set; }
        public long? FullImageSizeBytes { get; set; }
        public string? FullImageSha256 { get; set; }
    }
}
