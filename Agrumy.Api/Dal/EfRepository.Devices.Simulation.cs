using api.Models;

namespace api.Dal
{
    /// IDeviceRepository Simulation Mode members - forwarded to the standalone EfDeviceRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<DeviceSimulation?> DeviceSimulationGetAsync(int deviceID) => deviceRepository.DeviceSimulationGetAsync(deviceID);

        public Task DeviceSimulationSetAsync(int deviceID, DeviceSimulation value) => deviceRepository.DeviceSimulationSetAsync(deviceID, value);
    }
}
