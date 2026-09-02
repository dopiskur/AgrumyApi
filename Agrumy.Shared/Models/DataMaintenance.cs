namespace api.Models
{
    /// <summary>Roadmap #126: the one threshold set both "Optimize Old Data" and "Purge Old Data"
    /// dropdowns offer, applied consistently to each - server-side validated here so a hand-crafted
    /// API call can't sneak in an arbitrary cutoff.</summary>
    public static class DataMaintenanceThresholds
    {
        public static readonly IReadOnlyList<int> AllowedDays = [90, 180, 365, 730, 1825, 3650];
    }

    /// <summary>Body of POST /api/DataMaintenance/Optimize.</summary>
    public class DataMaintenanceRequest
    {
        public int OlderThanDays { get; set; }
    }

    /// <summary>Body of POST /api/DataMaintenance/Purge. ConfirmationPhrase must equal
    /// RequiredPhrase, checked server-side (not just by the Web form) since the API is reachable
    /// directly - same "at least as strict as #92" typed-confirmation gate the roadmap calls for.
    /// ShrinkAfterPurge only means anything on MariaDB/MySQL (see EfRepository.SensorData's
    /// PurgeOldSensorDataAsync); Postgres/TimescaleDB's drop_chunks() always reclaims disk space
    /// with no extra step, so Agrumy.Web never even asks the question on that provider.</summary>
    public class DataPurgeRequest
    {
        public const string RequiredPhrase = "PURGE";

        public int OlderThanDays { get; set; }
        public string? ConfirmationPhrase { get; set; }
        public bool ShrinkAfterPurge { get; set; }
    }

    /// <summary>Lets Agrumy.Web decide whether to show the MariaDB-only "shrink files on disk?"
    /// follow-up dialog (roadmap #126) without the Refit contract needing to know about
    /// api.Dal.DbProviderKind.</summary>
    public class DataMaintenanceProviderInfo
    {
        public bool IsMySql { get; set; }
    }
}
