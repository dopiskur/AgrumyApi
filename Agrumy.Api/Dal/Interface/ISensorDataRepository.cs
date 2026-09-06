using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    /// Telemetry facet of the data layer.
    public interface ISensorDataRepository
    {
        /// Persists a telemetry batch - deviceID/tenantID/deviceFarmUnitID/deviceFarmUnitZoneID come from the authenticated identity and are applied to every row; matching keys inside the JSON itself are ignored.
        Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceFarmUnitID, int? deviceFarmUnitZoneID);
        Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);

        /// Same JSON shape as SensorDataGetAsync, but time-bucket averaged across every device in the zone/unit instead of one device's own raw readings.
        Task<string> SensorDataZoneAverageGetAsync(int? tenantID, int deviceFarmUnitZoneID, int? timeRange, int? timeMDMY);
        Task<string> SensorDataUnitAverageGetAsync(int? tenantID, int deviceFarmUnitID, int? timeRange, int? timeMDMY);

        Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? sensorDataReportID);
        Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);

        /// Raw, untransformed rows for a whole tenant (tenant export), not shaped for chart consumption like SensorDataGetAsync - sinceUtc null means every row ever recorded.
        Task<IList<SensorData>> SensorDataExportGetAsync(int tenantID, DateTime? sinceUtc);

        /// Bulk-inserts already-remapped rows (tenant import) - the caller has resolved every id to its new value on the target server, this just persists them as-is.
        Task SensorDataImportAsync(IList<SensorData> rows);

        /// Downsamples every row older than cutoffUtc, per device, into one 5-minute-bucket average-without-outliers row, replacing the raw rows in place.
        Task OptimizeOldSensorDataAsync(DateTime cutoffUtc, CancellationToken ct);

        /// Deletes rows older than cutoffUtc outright (drop_chunks() on TimescaleDB, plain DELETE otherwise) - shrinkAfterPurge also runs OPTIMIZE TABLE on MariaDB/MySQL, whose DELETE never shrinks the .ibd file.
        Task PurgeOldSensorDataAsync(DateTime cutoffUtc, bool shrinkAfterPurge, CancellationToken ct);
    }
}
