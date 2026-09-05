namespace api.Models
{
    public class Tenant
    {
        public int? IDTenant { get; set; }
        public string? TenantName { get; set; }
    }

    /// <summary>One saved WiFi AP a tenant's admin can hand to a newly discovered device
    /// (roadmap #268) instead of typing it in again on every Register. Password is omitted from
    /// any list response the UI uses just to pick one (see DiscoveryApiController.Register).</summary>
    public class TenantWifiConfig
    {
        public int IDTenantWifiConfig { get; set; }
        public int TenantID { get; set; }
        public string Ssid { get; set; } = "";
        public string? Password { get; set; }
    }
}
