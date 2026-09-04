using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    /// <summary>Telemetry facet of the data layer.</summary>
    public interface ISensorDataRepository
    {
        /// <summary>
        /// Persist a telemetry batch. deviceID/tenantID/deviceUnitID/deviceUnitZoneID come from the
        /// authenticated device identity and are applied to every row; the same keys inside each JSON
        /// object are ignored, so a device cannot write telemetry against another device or tenant.
        /// </summary>
        Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID);
        Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        /// <summary>Same JSON shape as SensorDataGetAsync, but time-bucket averaged across every device in
        /// the zone/unit instead of one device's own raw readings - a metric only one device reports
        /// averages over just that value, so this also covers the "single contributor" case correctly.</summary>
        Task<string> SensorDataZoneAverageGetAsync(int? tenantID, int deviceUnitZoneID, int? timeRange, int? timeMDMY);
        Task<string> SensorDataUnitAverageGetAsync(int? tenantID, int deviceUnitID, int? timeRange, int? timeMDMY);

        Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? sensorDataReportID);
        Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);

        /// <summary>Downsamples every row older than cutoffUtc, per device, into one 5-minute-bucket
        /// average-without-outliers row, replacing the raw rows in place. Plain LINQ/EF throughout -
        /// identical on MariaDB and PostgreSQL, no TimescaleDB-specific SQL.</summary>
        Task OptimizeOldSensorDataAsync(DateTime cutoffUtc, CancellationToken ct);

        /// <summary>Deletes rows older than cutoffUtc outright - nothing survives, not even an
        /// aggregate. Uses drop_chunks() on a TimescaleDB hypertable or a plain DELETE otherwise;
        /// shrinkAfterPurge additionally runs OPTIMIZE TABLE on MariaDB/MySQL, whose DELETE never
        /// shrinks the underlying .ibd file on its own.</summary>
        Task PurgeOldSensorDataAsync(DateTime cutoffUtc, bool shrinkAfterPurge, CancellationToken ct);
    }
}
