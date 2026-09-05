namespace api.Models
{
    /// Allowed cutoffs for data maintenance actions, validated server-side so a hand-crafted API call can't pass an arbitrary value.
    public static class DataMaintenanceThresholds
    {
        public static readonly IReadOnlyList<int> AllowedDays = [90, 180, 365, 730, 1825, 3650];
    }

    /// Body of POST /api/DataMaintenance/Optimize.
    public class DataMaintenanceRequest
    {
        public int OlderThanDays { get; set; }
    }

    /// Body of POST /api/DataMaintenance/Purge; ConfirmationPhrase must match RequiredPhrase (checked server-side, not just by the Web form). ShrinkAfterPurge only applies on MariaDB/MySQL — Postgres/TimescaleDB reclaims space automatically.
    public class DataPurgeRequest
    {
        public const string RequiredPhrase = "PURGE";

        public int OlderThanDays { get; set; }
        public string? ConfirmationPhrase { get; set; }
        public bool ShrinkAfterPurge { get; set; }
    }

    /// Lets Agrumy.Web decide whether to show the MariaDB-only "shrink files on disk?" dialog without the Refit contract needing to know about api.Dal.DbProviderKind.
    public class DataMaintenanceProviderInfo
    {
        public bool IsMySql { get; set; }
    }
}
