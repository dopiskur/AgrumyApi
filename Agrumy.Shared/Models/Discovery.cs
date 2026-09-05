namespace api.Models
{
    /// Body of POST /api/Discovery/Report - scanning device identity comes exclusively from the authenticated apiId, same rule as DeviceApiController.PushEvent, not from this body.
    public class DiscoveryReportRequest
    {
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
    }

    /// Body of POST /api/Discovery/Scan - both null means Fleet-wide; ZoneID takes precedence over UnitID when both are given.
    public class DiscoveryScanRequest
    {
        public int? UnitID { get; set; }
        public int? ZoneID { get; set; }
    }

    /// One row per unique DiscoveredApMac in GET /api/Discovery/Results - see api.Utils.DiscoveryResultPicker for the best-report/tiebreak rule.
    public class DiscoveryResult
    {
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
        public int ScanningDeviceID { get; set; }
        public string? ScanningDeviceName { get; set; }
        public DateTime DateReported { get; set; }
    }
}
