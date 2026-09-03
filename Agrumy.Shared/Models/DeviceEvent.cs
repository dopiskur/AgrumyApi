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
    }

    /// <summary>Body of POST /api/Device/Event; deliberately has no device/tenant identity field — the caller's apiId (see api.Security.DeviceAuth) is the only trusted source for that.</summary>
    public class DeviceEventPush
    {
        public string? EventType { get; set; }
        public string? Message { get; set; }
        /// <summary>Set only when EventType=CommandExecuted; identifies which pending command this confirms.</summary>
        public int? CommandId { get; set; }
    }

    /// <summary>CreatedAt is server-side, not device-reported — a device mid-NoInternet event may not have NTP sync yet.</summary>
    public class DeviceEvent
    {
        public int? IDEventDevice { get; set; }
        public int? DeviceID { get; set; }
        public string? EventType { get; set; }
        public string? Message { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
