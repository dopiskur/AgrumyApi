using api.Models;

namespace api.Dal
{
    /// IDeviceRepository fixed-type-list members - forwarded to the standalone EfDeviceRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<IList<DeviceRole>> DeviceRoleGetAsync() => deviceRepository.DeviceRoleGetAsync();

        public Task<IList<DeviceType>> DeviceTypeGetAsync() => deviceRepository.DeviceTypeGetAsync();

        public Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync() => deviceRepository.DeviceTypeServiceGetAsync();

        public Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync() => deviceRepository.DeviceTypeRelayGetAsync();

        public Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync() => deviceRepository.DeviceTypeSensorGetAsync();
    }
}
