namespace api.Dal.Entities
{
    /// TenantID null means the shared global sentinel row (IDDeviceFarmUnit=0 "Default", see EfRepository.SeedDeviceFarmUnitSentinelsAsync), not a real per-tenant Unit.
    /// Roadmap #384 - top-level organizational grouping ABOVE Unit within the same tenant (a physical farm/site); optional, a DeviceFarmUnit not yet assigned to one has DeviceFarmID null.
    public class DeviceFarmRow
    {
        public int IDDeviceFarm { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceFarmName { get; set; }
    }

    public class DeviceFarmUnitRow
    {
        public int IDDeviceFarmUnit { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceFarmUnitName { get; set; }
        public bool? ZoneEnabled { get; set; }
        // Roadmap #384 - optional (a Farm-less Unit stays valid, no default-farm backfill).
        public int? DeviceFarmID { get; set; }
    }

    /// DeviceFarmUnitID is the real "Unit contains many Zones" FK (see db/migrations/2026-09-02-deviceunit-zone-containment.sql) - TenantID null means the shared global sentinel row (IDDeviceFarmUnitZone=0 "Disabled"), same convention as DeviceFarmUnitRow.
    public class DeviceFarmUnitZoneRow
    {
        public int IDDeviceFarmUnitZone { get; set; }
        public int? TenantID { get; set; }
        public int DeviceFarmUnitID { get; set; }
        public string? DeviceFarmUnitZoneName { get; set; }

        // See api.Models.DeviceFarmUnitZone's own copy of these for the full explanation.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // See api.Models.DeviceFarmUnitZone.SkipWaterPumpWhenRainPredicted.
        public bool SkipWaterPumpWhenRainPredicted { get; set; }

        // See api.Models.DeviceFarmUnitZone's own copy of these for the full explanation (roadmap #234).
        public double? TankCapacityLiters { get; set; }
        public int? WaterLevelRawEmpty { get; set; }
        public int? WaterLevelRawFull { get; set; }
        public DateTime? TankRefillNotifiedAt { get; set; }

        // See api.Models.DeviceFarmUnitZone's own copy of these for the full explanation (roadmap #219).
        public int? HeatingMaxRunSeconds { get; set; }
        public int? VentilationMaxRunSeconds { get; set; }
    }

    /// See api.Models.DeviceFarmUnitZoneRule - Conditions is a JSON array of RuleCondition, (de)serialized at the application layer, not a native JSON column type. Exactly one of DeviceFarmUnitZoneID/DeviceFarmUnitID is set for Zone/Unit scope, both null for Global (per-tenant) scope.
    public class DeviceFarmUnitZoneRuleRow
    {
        public int IDDeviceFarmUnitZoneRule { get; set; }
        public int TenantID { get; set; }
        // Roadmap #384 - exactly one of DeviceFarmID/DeviceFarmUnitID/DeviceFarmUnitZoneID is set (Farm/Unit/Zone scope); all three null means Global. Enforced in DeviceFarmUnitApiController, not the DB, same as the other two.
        public int? DeviceFarmID { get; set; }
        public int? DeviceFarmUnitID { get; set; }
        public int? DeviceFarmUnitZoneID { get; set; }
        public int ActionType { get; set; }
        public int? RelayFunction { get; set; }
        public int? SensorMetric { get; set; }
        public string Conditions { get; set; } = "[]";
        public string? NotificationSubject { get; set; }
        public string? NotificationBody { get; set; }
    }

    /// Per-(rule, zone) dedup latch for api.BackgroundWorkers.RuleNotificationEvaluator - a rule scoped above Zone level is evaluated independently against every zone it reaches, so the "already notified, don't re-fire every tick" state is keyed per zone, not just per rule.
    public class RuleNotificationStateRow
    {
        public int IDRuleNotificationState { get; set; }
        public int RuleID { get; set; }
        public int DeviceFarmUnitZoneID { get; set; }
        public bool WasTrue { get; set; }
        public DateTime? LastFiredAtUtc { get; set; }
    }

    public class DeviceRoleRow
    {
        public int IDDeviceRole { get; set; }
        public string? DeviceRoleName { get; set; }
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

    /// One physically-wired relay slot assigned to a RelayFunction - only assigned slots get a row, replacing the fixed Relay1..Relay8 columns that used to live on DeviceConfigControllerRow.
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

    /// Per-metric sensor-reading overrides for an EXISTING physical device (Simulation Mode) - one row per device, upserted from the Web Simulation page. A null field means "use the real reading"; Enabled=false means every field is ignored regardless of value.
    public class DeviceSimulationRow
    {
        public int DeviceID { get; set; }
        public bool Enabled { get; set; }
        public double? Temperature { get; set; }
        public double? SoilTemperature { get; set; }
        public double? Humidity { get; set; }
        public int? Battery { get; set; }
        public int? Moisture { get; set; }
        public int? Light { get; set; }
        public int? Co2 { get; set; }
        public int? Tvoc { get; set; }
        public double? Barometer { get; set; }
        public double? LiquidPH { get; set; }
        public int? RainLevel { get; set; }
        public int? WaterLevel { get; set; }
        public int? Wind { get; set; }
    }

    /// Purely a server-internal registry of which device rows VirtualDeviceRunnerBackgroundService is responsible for driving - never exposed on any wire contract, never read by the device-facing endpoints themselves (Register/Authenticate/Config/SensorData/ControllerData have no idea a caller is virtual). A device with no row here is an ordinary, real device.
    public class DeviceVirtualRow
    {
        public int DeviceID { get; set; }
        public DateTime DateCreated { get; set; }
    }

    public class DeviceRow
    {
        public int IDDevice { get; set; }
        public int TenantID { get; set; } // Non-nullable, matching the DB column (NOT NULL DEFAULT 0).
        public int? DeviceRoleID { get; set; }
        public int? DeviceFarmUnitID { get; set; }
        public int? DeviceFarmUnitZoneID { get; set; }
        public int? DeviceConfigSensorID { get; set; }
        public int? DeviceConfigControllerID { get; set; }
        public int? DeviceTypeServiceID { get; set; }
        public string? DeviceName { get; set; }
        public string? MacAddress { get; set; }
        // Admin-set fallback for a device whose firmware build never reports a Kit (generic esp32dev/esp32s3usbotg) - BuildFleetStatusesAsync's ControllerCapable check falls back to this only when the diagnostic DeviceTypeID is unset. Real FK to deviceType.IDDeviceType, not the Kit string.
        public int? ManualDeviceTypeID { get; set; }
        public string ApiId { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string? ServicePoint { get; set; }
        public string? ServicePublicKey { get; set; }
        public int? SleepSeconds { get; set; }
        public bool? SleepDeepEnabled { get; set; }
        // Roadmap #383 - see api.Models.Device.LoRaGatewayEnabled.
        public bool? LoRaGatewayEnabled { get; set; }
        public bool? DeviceSensorEnabled { get; set; }
        public bool? DeviceControllerEnabled { get; set; }
        public bool? BatteryEnabled { get; set; }
        public bool? Enabled { get; set; }
        public bool? Debug { get; set; }
        public bool? Reboot { get; set; }
        public bool? Reset { get; set; }
        public bool? FirmwareUpdate { get; set; }
        public string? FirmwareTargetVersion { get; set; } // See api.Models.Device.FirmwareTargetVersion.
        public int? ConfigVersion { get; set; }
        public int CommandVersion { get; set; } // See api.Models.DeviceConfig.CommandVersion.
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        public bool IsGateway { get; set; }
        public int? GatewayProfile { get; set; }

        public DateTime? LastFullConfigSentAt { get; set; } // See api.Models.Device.LastFullConfigSentAt.
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
        public int? ActiveKey { get; set; } // Mirrors ActionType while active, NULL once terminal; backs the unique (DeviceID, ActiveKey) index IssueCommandAsync's dedup relies on.
        public string? Payload { get; set; }
    }

    /// One active manual actuation (roadmap #219) - upserted on (DeviceID, RelayFunction), so starting a new command for an already-active function replaces it rather than stacking rows.
    public class DeviceManualOverrideRow
    {
        public int IDDeviceManualOverride { get; set; }
        public int DeviceID { get; set; }
        public int TenantID { get; set; }
        public int RelayFunction { get; set; }
        public int Mode { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public int? TargetMetric { get; set; }
        public double? TargetThreshold { get; set; }
        public double? TargetHysteresis { get; set; }
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
        public DateTime? OfflineNotifiedAt { get; set; } // When OfflineAlertBackgroundService last notified admins about the device's current offline streak; one notification per streak, not per tick.
        public DateTime? LowBatteryNotifiedAt { get; set; } // Same dedup-by-streak rule as OfflineNotifiedAt, but for LowBatteryAlertEvaluator.
        public string? FirmwareVersion { get; set; }
        public string? Board { get; set; } // See api.Models.DeviceConfigPoll.Board.
        // Real FK to deviceType.IDDeviceType, resolved from the firmware-reported Kit string (api.Models.DeviceConfigPoll.Kit) by DeviceDiagnosticUpsertAsync - the wire protocol still carries a string, only storage is numeric.
        public int? DeviceTypeID { get; set; }
    }

    /// Catalog of recognized physical device kits - IDDeviceType is the real PK, Kit a unique display/build-flag string (e.g. "KC868-A6") every referencing table now FKs to by id, not by name. PinoutJson is an unopinionated per-kit GPIO layout blob, null for a kit nobody has documented pinout for yet, including every auto-registered one (see DeviceDiagnosticUpsertAsync).
    public class DeviceTypeRow
    {
        public int IDDeviceType { get; set; }
        public string Kit { get; set; } = "";
        public bool ControllerCapable { get; set; }
        public string? PinoutJson { get; set; }
    }

    // Board/Source/FileName/SizeBytes/Sha256/PublishedAt - see api.Models.DeviceFirmware for what each means; DeviceTypeID is the legacy key.
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
