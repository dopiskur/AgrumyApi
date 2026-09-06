namespace api.Models
{
    public enum DeviceEventType
    {
        NoInternet = 1,
        AuthFailed = 2,
        ConfigSyncFailed = 3,
        ConfigApplied = 4,
        CrashLoopRollback = 5,
        OtaFailed = 6,
        BufferDiscarded = 7,
        // Server-detected (not device-pushed).
        Offline = 8,
        CommandExecuted = 9,
        // Server-detected (not device-pushed).
        FirmwareUpdated = 10,
        // Server-detected (not device-pushed).
        LowBattery = 11,
        SafetyLimitTripped = 12,
        // Message carries a compact core-dump summary (task/pc/cause/backtrace); full symbolication still needs firmware.elf + addr2line offline.
        Crash = 13,
        // Wire.endTransmission() failed writing the PCF8574 relay shadow.
        I2CFault = 14,
        // A rule had an unrecognized/over-cap condition and was rejected whole rather than silently truncated.
        RuleRejected = 15,
        // A relay-function evaluator hit a NaN sensor reading.
        SensorStale = 16,
        // ESP.getFreeHeap() dropped below the device's own reboot threshold, replacing the old raw-failed-config-cycle-count trigger.
        LowMemoryReboot = 17,
        // LoRaGatewayEnabled was requested but radio.begin() failed - no LoRa chip physically present/wired (roadmap #383).
        LoRaHardwareNotDetected = 18,
    }

    /// Body of POST /api/Device/Event; deliberately has no device/tenant identity field — the caller's apiId (see api.Security.DeviceAuth) is the only trusted source for that.
    public class DeviceEventPush
    {
        public string? EventType { get; set; }
        public string? Message { get; set; }
        /// Set only when EventType=CommandExecuted; identifies which pending command this confirms.
        public int? CommandId { get; set; }
    }

    /// CreatedAt is server-side, not device-reported — a device mid-NoInternet event may not have NTP sync yet.
    public class DeviceEvent
    {
        public int? IDEventDevice { get; set; }
        public int? DeviceID { get; set; }
        public string? EventType { get; set; }
        public string? Message { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
    }
}
