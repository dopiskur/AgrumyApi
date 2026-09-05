namespace api.Models
{
    /// <summary>Body of POST /api/Discovery/Report - scanning device identity comes exclusively from
    /// the authenticated apiId, same rule as DeviceApiController.PushEvent, not from this body.</summary>
    public class DiscoveryReportRequest
    {
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
    }
}
