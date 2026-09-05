namespace api.Models
{
    /// <summary>Body of POST /api/Discovery/Report - scanning device identity comes exclusively from
    /// the authenticated apiId, same rule as DeviceApiController.PushEvent, not from this body.</summary>
    public class DiscoveryReportRequest
    {
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
    }

    /// <summary>Body of POST /api/Discovery/Scan - both null means Fleet-wide; ZoneID takes
    /// precedence over UnitID when both are given (the Zone page's scan passes both for context,
    /// but the zone alone already pins the scope down).</summary>
    public class DiscoveryScanRequest
    {
        public int? UnitID { get; set; }
        public int? ZoneID { get; set; }
    }

    /// <summary>One row per unique DiscoveredApMac in GET /api/Discovery/Results - see
    /// api.Utils.DiscoveryResultPicker for the best-report/tiebreak rule.</summary>
    public class DiscoveryResult
    {
        public string DiscoveredApMac { get; set; } = "";
        public int? Rssi { get; set; }
        public int ScanningDeviceID { get; set; }
        public string? ScanningDeviceName { get; set; }
        public DateTime DateReported { get; set; }
    }
}
