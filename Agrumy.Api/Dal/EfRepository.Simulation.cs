namespace api.Dal
{
    /// ISimulationRepository members - forwarded to the standalone EfSimulationRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task VirtualDeviceRegisterAsync(int deviceID) => simulationRepository.VirtualDeviceRegisterAsync(deviceID);

        public Task<IList<int>> VirtualDeviceIdsGetAsync() => simulationRepository.VirtualDeviceIdsGetAsync();

        public Task<IList<int>> VirtualDeviceIdsGetAsync(int? tenantID) => simulationRepository.VirtualDeviceIdsGetAsync(tenantID);

        public Task VirtualDeviceDeleteAsync(int deviceID, int tenantID) => simulationRepository.VirtualDeviceDeleteAsync(deviceID, tenantID);
    }
}
