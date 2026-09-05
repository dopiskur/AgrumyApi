namespace api.Models
{
    /// Where an import lands: ByName matches/creates a tenant by exact name; AsSentinel targets TenantID=0 and is only reachable via TenantApiController.ImportAsSentinel while the bootstrap Global Admin is still unclaimed (ITenantRepository.TenantZeroIsEmptyAsync).
    public enum TenantImportTarget
    {
        ByName = 0,
        AsSentinel = 1,
    }

    /// One exported user: the User DTO plus its portable PBKDF2 hash/salt and role NAMES (not the install-specific userRole.IDUserRole, which would need remapping).
    public class TenantExportUser
    {
        public User User { get; set; } = new();
        public string? PwdHash { get; set; }
        public string? PwdSalt { get; set; }
        public IList<string> Roles { get; set; } = [];
    }

    /// One exported device: Device+Sensor/Controller (DeviceUpdate's grouping), with ApiId/ApiKey carried as separate fields since both are [JsonIgnore] on Device and would otherwise silently drop from the export.
    public class TenantExportDevice
    {
        public Device Device { get; set; } = new();
        public string? ApiId { get; set; }
        public string? ApiKey { get; set; }
        public DeviceConfigSensor? Sensor { get; set; }
        public DeviceConfigController? Controller { get; set; }
    }

    /// The full portable snapshot of one tenant (excludes install-wide ServerConfig/firmware catalog, includes SensorData only when opt-in) - SENSITIVE (password hashes, device ApiKeys), never persisted server-side, streamed directly to the admin's browser.
    public class TenantExport
    {
        public const string CurrentFormatVersion = "1";
        public string FormatVersion { get; set; } = CurrentFormatVersion;
        public DateTime ExportedAtUtc { get; set; }
        public string? SourceTenantName { get; set; }

        public IList<TenantExportUser> Users { get; set; } = [];
        public IList<DeviceUnit> Units { get; set; } = [];
        public IList<DeviceUnitZone> Zones { get; set; } = [];
        public IList<DeviceUnitZoneRule> ZoneRules { get; set; } = [];
        public IList<TenantExportDevice> Devices { get; set; } = [];

        public bool IncludesSensorData { get; set; }
        public IList<SensorData>? SensorData { get; set; }
    }

    /// Body of POST /api/Tenant/Import (ByName only - ImportAsSentinel takes a bare TenantExport, no target name needed).
    public class TenantImportRequest
    {
        public TenantExport? Export { get; set; }
        /// Required for ByName - matched case-sensitively against an existing tenant, or used to create a new one if none matches.
        public string? TargetTenantName { get; set; }
    }

    /// What actually happened - counts, not the imported rows themselves (the caller already has those, in the export they just submitted).
    public class TenantImportResult
    {
        public int TargetTenantId { get; set; }
        public string? TargetTenantName { get; set; }
        public int UsersImported { get; set; }
        public int UsersSkipped { get; set; }
        public int DevicesSkipped { get; set; }
        /// Human-readable reason for each skipped user/device (unique-constraint conflicts with something already on the target).
        public IList<string> SkippedReasons { get; set; } = [];
        public int DevicesImported { get; set; }
        public int UnitsImported { get; set; }
        public int ZonesImported { get; set; }
        public int ZoneRulesImported { get; set; }
        public int SensorDataRowsImported { get; set; }
    }
}
