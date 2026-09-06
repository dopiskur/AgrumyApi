using api.Models;

namespace api.Dal
{
    /// IDeviceRepository's legacy board-less OTA lookup - forwarded to the standalone EfDeviceRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID) => deviceRepository.DeviceFirmwareLatestGetAsync(deviceTypeID);
    }
}
