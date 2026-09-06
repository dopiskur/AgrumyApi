namespace api.Dal.Interface
{
    /// The server-internal registry of which devices are fully virtual, used only by the Simulation admin endpoints and VirtualDeviceRunnerBackgroundService - never consulted by any device-facing endpoint.
    public interface ISimulationRepository
    {
        Task VirtualDeviceRegisterAsync(int deviceID);

        /// Every virtual device across every tenant - the runner is not tenant-scoped, it drives every one of them regardless of who owns it.
        Task<IList<int>> VirtualDeviceIdsGetAsync();

        /// Virtual device ids owned by tenantID, for the Web listing page (or every one when tenantID is null, GlobalAdmin's own-tenant-only rule still enforced by the caller).
        Task<IList<int>> VirtualDeviceIdsGetAsync(int? tenantID);

        /// Deletes sensorData/controllerData/the registry row/the device itself (in that order) - a virtual device's synthetic telemetry has no historical value once it's gone, unlike a real device's (DeviceDeleteAsync alone does not touch sensorData).
        Task VirtualDeviceDeleteAsync(int deviceID, int tenantID);
    }
}
