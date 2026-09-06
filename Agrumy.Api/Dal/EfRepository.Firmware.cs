using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IFirmwareRepository members - forwarded to the standalone EfFirmwareRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<IList<DeviceFirmware>> FirmwareListAsync() => firmwareRepository.FirmwareListAsync();

        public Task<DeviceFirmware?> FirmwareGetAsync(int idDeviceFirmware) => firmwareRepository.FirmwareGetAsync(idDeviceFirmware);

        public Task<IList<DeviceFirmware>> FirmwareListForBoardAsync(string board, IReadOnlyCollection<FirmwareSource> sources) =>
            firmwareRepository.FirmwareListForBoardAsync(board, sources);

        public Task<int> FirmwareAddAsync(DeviceFirmware firmware) => firmwareRepository.FirmwareAddAsync(firmware);

        public Task FirmwareDeleteAsync(int idDeviceFirmware) => firmwareRepository.FirmwareDeleteAsync(idDeviceFirmware);

        public Task<int> FirmwareDeleteBySourceAsync(FirmwareSource source) => firmwareRepository.FirmwareDeleteBySourceAsync(source);

        public Task<int> FirmwareReplaceSourceRowsAsync(FirmwareSource source, IReadOnlyList<DeviceFirmware> rows) =>
            firmwareRepository.FirmwareReplaceSourceRowsAsync(source, rows);

        public Task DeviceFirmwareUpdateSetAsync(int idDevice, bool update, string? targetVersion) =>
            firmwareRepository.DeviceFirmwareUpdateSetAsync(idDevice, update, targetVersion);

        public Task<string?> DeviceBoardGetAsync(int idDevice) => firmwareRepository.DeviceBoardGetAsync(idDevice);
    }
}
