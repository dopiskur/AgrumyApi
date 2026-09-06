using System.Text.Json.Nodes;
using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// ISensorDataRepository members - forwarded to the standalone EfSensorDataRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID) =>
            sensorDataRepository.SensorDataPushAsync(jsonArray, deviceID, tenantID, deviceUnitID, deviceUnitZoneID);

        public Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport) =>
            sensorDataRepository.SensorDataGetAsync(tenantID, deviceID, timeRange, timeMDMY, buildReport);

        public Task<string> SensorDataZoneAverageGetAsync(int? tenantID, int deviceUnitZoneID, int? timeRange, int? timeMDMY) =>
            sensorDataRepository.SensorDataZoneAverageGetAsync(tenantID, deviceUnitZoneID, timeRange, timeMDMY);

        public Task<string> SensorDataUnitAverageGetAsync(int? tenantID, int deviceUnitID, int? timeRange, int? timeMDMY) =>
            sensorDataRepository.SensorDataUnitAverageGetAsync(tenantID, deviceUnitID, timeRange, timeMDMY);

        public Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? sensorDataReportID) =>
            sensorDataRepository.SensorDataReportGetAsync(tenantID, getData, deviceID, sensorDataReportID);

        public Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY) =>
            sensorDataRepository.SensorDataDeleteAsync(tenantID, deviceID, timeRange, timeMDMY);

        public Task<IList<SensorData>> SensorDataExportGetAsync(int tenantID, DateTime? sinceUtc) => sensorDataRepository.SensorDataExportGetAsync(tenantID, sinceUtc);

        public Task SensorDataImportAsync(IList<SensorData> rows) => sensorDataRepository.SensorDataImportAsync(rows);

        public Task OptimizeOldSensorDataAsync(DateTime cutoffUtc, CancellationToken ct) => sensorDataRepository.OptimizeOldSensorDataAsync(cutoffUtc, ct);

        public Task PurgeOldSensorDataAsync(DateTime cutoffUtc, bool shrinkAfterPurge, CancellationToken ct) => sensorDataRepository.PurgeOldSensorDataAsync(cutoffUtc, shrinkAfterPurge, ct);
    }
}
