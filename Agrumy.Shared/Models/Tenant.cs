namespace api.Models
{
    public class Tenant
    {
        public int? IDTenant { get; set; }
        public string? TenantName { get; set; }
        // IANA id; DeviceConfigBuilder uses it (falling back to AgrumySettings.ScheduleTimeZone, then UTC) to compute the UtcOffsetSeconds every device in this tenant gets - null/empty means the tenant hasn't set one of its own.
        public string? ScheduleTimeZone { get; set; }
    }

    /// One saved WiFi AP a tenant's admin can hand to a newly discovered device instead of typing it in again on every Register; Password is omitted from any list response the UI uses just to pick one (see DiscoveryApiController.Register).
    public class TenantWifiConfig
    {
        public int IDTenantWifiConfig { get; set; }
        public int TenantID { get; set; }
        public string Ssid { get; set; } = "";
        public string? Password { get; set; }
    }
}
