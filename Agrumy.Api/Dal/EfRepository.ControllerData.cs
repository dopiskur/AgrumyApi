using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IControllerDataRepository members - forwarded to the standalone EfControllerDataRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task ControllerDataPushAsync(int deviceID, int tenantID, IList<ControllerDataPush> entries) =>
            controllerDataRepository.ControllerDataPushAsync(deviceID, tenantID, entries);

        public Task<IList<ControllerDataStatus>> ControllerDataGetAsync(int deviceID) => controllerDataRepository.ControllerDataGetAsync(deviceID);
    }
}
