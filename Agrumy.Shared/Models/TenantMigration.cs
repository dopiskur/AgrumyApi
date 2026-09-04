namespace api.Models
{
    /// <summary>Where an import lands. ByName ties to a tenant matched (or created) by exact
    /// name - a normal cloud-to-cloud or private-to-private move. AsSentinel targets TenantID=0,
    /// the "become the sole tenant on my own fresh self-hosted server" path - only reachable while
    /// that server's bootstrap Global Admin is still unclaimed (see
    /// ITenantRepository.TenantZeroIsEmptyAsync), and only via
    /// TenantApiController.ImportAsSentinel, not the normal admin-authenticated Import.</summary>
    public enum TenantImportTarget
    {
        ByName = 0,
        AsSentinel = 1,
    }

    /// <summary>One user in a TenantExport - the User DTO plus what TenantExport needs beyond it:
    /// the password hash/salt (portable - every Agrumy install hashes with the same PBKDF2
    /// parameters) and the role names UserRoleNamesGetAsync already resolves by name rather than
    /// the install-specific userRole.IDUserRole a raw id would need remapping for.</summary>
    public class TenantExportUser
    {
        public User User { get; set; } = new();
        public string? PwdHash { get; set; }
        public string? PwdSalt { get; set; }
        public IList<string> Roles { get; set; } = [];
    }

    /// <summary>One device in a TenantExport - Device plus its Sensor/Controller config, the same
    /// grouping DeviceUpdate already uses for the admin-edit form. ApiId/ApiKey are carried as
    /// their OWN fields, not read off the nested Device: both are [JsonIgnore] on api.Models.Device
    /// (never serialized to a normal device-API response, by design), which would otherwise
    /// silently drop them from the exported JSON too - defeating the "keep the same ApiKey"
    /// decision this whole export exists to honor.</summary>
    public class TenantExportDevice
    {
        public Device Device { get; set; } = new();
        public string? ApiId { get; set; }
        public string? ApiKey { get; set; }
        public DeviceConfigSensor? Sensor { get; set; }
        public DeviceConfigController? Controller { get; set; }
    }

    /// <summary>The full portable snapshot of one tenant - everything TenantID-scoped except
    /// SensorData, which is opt-in (IncludesSensorData) given its potential volume. Deliberately
    /// excludes ServerConfig (confirmed to carry no TenantID - it is install-wide) and the firmware
    /// catalog (also install-wide).
    ///
    /// Every *.IDXxx field on every nested DTO is the SOURCE server's id, meaningful only for
    /// stitching internal references (e.g. DeviceUnitZone.DeviceUnitID) back together during
    /// import - TenantImportService discards every one of them and lets the target database assign
    /// fresh ids, so an import can never collide with anything already on the target.
    ///
    /// SENSITIVE: carries password hashes/salts and device ApiKeys - handle like any other
    /// credential bundle (see TenantApiController.Export's remarks). Never persisted server-side;
    /// generated and streamed directly to the requesting admin's browser.</summary>
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

    /// <summary>Body of POST /api/Tenant/Import (ByName only - ImportAsSentinel takes a bare
    /// TenantExport, no target name needed since AsSentinel always means "TenantID=0").</summary>
    public class TenantImportRequest
    {
        public TenantExport? Export { get; set; }
        /// <summary>Required for ByName - matched case-sensitively against an existing tenant, or
        /// used to create a new one if none matches.</summary>
        public string? TargetTenantName { get; set; }
    }

    /// <summary>What actually happened - counts, not the imported rows themselves (the caller
    /// already has those, in the export they just submitted).</summary>
    public class TenantImportResult
    {
        public int TargetTenantId { get; set; }
        public string? TargetTenantName { get; set; }
        public int UsersImported { get; set; }
        public int UsersSkipped { get; set; }
        public int DevicesSkipped { get; set; }
        /// <summary>Human-readable reason for each skipped user/device (global-unique-constraint
        /// conflicts with something already on the target) - covers both counters above.</summary>
        public IList<string> SkippedReasons { get; set; } = [];
        public int DevicesImported { get; set; }
        public int UnitsImported { get; set; }
        public int ZonesImported { get; set; }
        public int ZoneRulesImported { get; set; }
        public int SensorDataRowsImported { get; set; }
    }
}
