using api.Models;

namespace api.Dal.Interface
{
    /// Real-time relay on/off state - "/api/ControllerData", parallel to ISensorDataRepository's SensorData push/read but upserted (current state) rather than appended (time series).
    public interface IControllerDataRepository
    {
        Task ControllerDataPushAsync(int deviceID, int tenantID, IList<ControllerDataPush> entries);

        Task<IList<ControllerDataStatus>> ControllerDataGetAsync(int deviceID);
    }
}
