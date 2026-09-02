namespace api.Models
{
    /// <summary>Closed set of device event types (roadmap #28) - stored as the legacy `eventDevice.EventID`
    /// int column, never as free text, so the event log stays queryable/groupable instead of drifting
    /// into inconsistent ad-hoc strings.</summary>
    public enum DeviceEventType
    {
        NoInternet = 1,
        AuthFailed = 2,
        ConfigSyncFailed = 3,
        ConfigApplied = 4,
        CrashLoopRollback = 5,
        OtaFailed = 6,
        // Roadmap #9: the device dropped buffered sensor data because its LittleFS partition hit
        // the 70% cap - deliberate loss on the device's side, but the operator should know.
        BufferDiscarded = 7,
        // Roadmap #40: server-detected, not device-pushed - written by OfflineAlertBackgroundService
        // when a device crosses ComputeOnline's threshold, so the transition shows in the same
        // Events timeline as every device-reported event.
        Offline = 8,
        // Roadmap #34: the device's post-execution confirmation for a deviceCommand (Reboot never
        // reaches this - it has no "after" to report from - ForceOTA/ForceConfigSync do). Message
        // carries the outcome ("success" / "failed: <reason>"); CommandId (DeviceEventPush) links
        // it back to the specific command row so PushEvent can call MarkExecutedAsync.
        CommandExecuted = 9,
        // Roadmap #93: server-detected, not device-pushed - written by DeviceApiController.GetConfig
        // when a heartbeat first reports the version an admin asked for (Device.FirmwareUpdate /
        // FirmwareTargetVersion), which is also when those two flags are cleared.
        FirmwareUpdated = 10,
        // Roadmap #12: server-detected, not device-pushed - written by LowBatteryAlertEvaluator
        // when the latest sensorData.Battery reading crosses ServerConfig.BatteryLowThreshold,
        // same dedup-by-streak pattern as Offline above (deviceDiagnostic.LowBatteryNotifiedAt).
        LowBattery = 11,
    }

    /// <summary>Body of POST /api/Device/Event. Deliberately carries no device/tenant identity field -
    /// see api.Security.DeviceAuth, the caller's apiId is the only source of truth for that (roadmap #47
    /// precedent: never trust identity fields a payload could lie about).</summary>
    public class DeviceEventPush
    {
        public string? EventType { get; set; }
        public string? Message { get; set; }
        /// <summary>Roadmap #34: present only alongside EventType=CommandExecuted - which
        /// deviceCommand row this confirms, so PushEvent can mark it Executed.</summary>
        public int? CommandId { get; set; }
    }

    /// <summary>One row from GET /api/Device/Events - server-side CreatedAt, not a device-reported
    /// timestamp, because a device mid-"NoInternet" event may not have NTP sync yet.</summary>
    public class DeviceEvent
    {
        public int? IDEventDevice { get; set; }
        public int? DeviceID { get; set; }
        public string? EventType { get; set; }
        public string? Message { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
