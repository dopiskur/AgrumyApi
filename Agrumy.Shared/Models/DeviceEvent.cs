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
    }

    /// <summary>Body of POST /api/Device/Event. Deliberately carries no device/tenant identity field -
    /// see api.Security.DeviceAuth, the caller's apiId is the only source of truth for that (roadmap #47
    /// precedent: never trust identity fields a payload could lie about).</summary>
    public class DeviceEventPush
    {
        public string? EventType { get; set; }
        public string? Message { get; set; }
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
