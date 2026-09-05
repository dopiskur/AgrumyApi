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

    /// <summary>Body of POST /api/Discovery/Register. WifiConfigId/Ssid/WifiPassword/SaveWifiForLater
    /// are only needed depending on how many TenantWifiConfig rows the tenant already has - see
    /// DiscoveryApiController.Register for the 0/1/many branching and DiscoveryRegisterOutcome for
    /// how the caller is told which one applies.</summary>
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

    /// <summary>Response of POST /api/Discovery/Register. WifiChoices is populated only for
    /// WifiConfigChoiceRequired - always Password-stripped, for a picker dropdown, never for display.</summary>
    public class DiscoveryRegisterResult
    {
        public DiscoveryRegisterOutcome Outcome { get; set; }
        public IList<TenantWifiConfig>? WifiChoices { get; set; }
    }

    /// <summary>The DeviceCommand.Payload JSON for a ProvisionDevice command - what the winning
    /// scanning device needs to complete roadmap #268 step 6 (connect to DiscoveredApMac as a
    /// client, POST these to its WiFiManager captive portal). DeviceName/UnitID/ZoneID ride along
    /// for a future step to apply once the device completes its own real /api/Device/Register -
    /// nothing consumes them yet.</summary>
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
