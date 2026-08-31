using api.Models;
using System.Text.Json.Nodes;

namespace api.Dal.Interface
{
    /// <summary>Telemetry facet of the data layer (roadmap #74).</summary>
    public interface ISensorDataRepository
    {
        /// <summary>
        /// Persist a telemetry batch. deviceID/tenantID/deviceUnitID/deviceUnitZoneID come from the
        /// authenticated device identity and are applied to every row; the same keys inside each JSON
        /// object are ignored, so a device cannot write telemetry against another device or tenant.
        /// </summary>
        Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID);
        Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport);
        Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? sensorDataReportID);
        Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY);
    }
}
