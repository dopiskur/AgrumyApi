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
        public int TenantID { get; set; }
        public DateTime DateReported { get; set; }
    }

    /// Body of POST /api/Discovery/Register - WifiConfigId/Ssid/WifiPassword/SaveWifiForLater are needed depending on how many TenantWifiConfig rows the tenant already has, per DiscoveryApiController.Register's branching (see DiscoveryRegisterOutcome for how the caller learns which one applies).
    public class DiscoveryRegisterRequest
    {
        public string DiscoveredApMac { get; set; } = "";
        public string? DeviceName { get; set; }
        public int? UnitID { get; set; }
        public int? ZoneID { get; set; }

        public int? WifiConfigId { get; set; }
        public string? Ssid { get; set; }
        public string? WifiPassword { get; set; }
        public bool SaveWifiForLater { get; set; }
    }

    public enum DiscoveryRegisterOutcome
    {
        Success,
        ApMacNotFound,
        WifiCredentialsRequired,
        WifiConfigChoiceRequired,
        AlreadyPending,
    }

    /// Response of POST /api/Discovery/Register; WifiChoices is populated only for WifiConfigChoiceRequired, always Password-stripped for a picker dropdown, never for display.
    public class DiscoveryRegisterResult
    {
        public DiscoveryRegisterOutcome Outcome { get; set; }
        public IList<TenantWifiConfig>? WifiChoices { get; set; }
    }

    /// The DeviceCommand.Payload JSON for a ProvisionDevice command - what the winning scanning device needs to connect to DiscoveredApMac as a client and POST these to its WiFiManager captive portal; DeviceName/UnitID/ZoneID ride along for a future step once the device completes its own /api/Device/Register, unused today.
    public class DiscoveryProvisionPayload
    {
        public string Username { get; set; } = "";
        public string Pin { get; set; } = "";
        public string DiscoveredApMac { get; set; } = "";
        public string Ssid { get; set; } = "";
        public string WifiPassword { get; set; } = "";
        public string? DeviceName { get; set; }
        public int? UnitID { get; set; }
        public int? ZoneID { get; set; }
        /// This API's own host (see DiscoveryApiController.PublicHost) - not the scanning device's own, possibly-stale deviceConfig.servicePoint.
        public string? ServicePoint { get; set; }
    }
}
