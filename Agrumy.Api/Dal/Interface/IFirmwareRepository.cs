using api.Models;

namespace api.Dal.Interface
{
    /// Firmware catalog facet: raw deviceFirmware rows and per-device update flags only - source selection, semver ordering, download and storage live in api.Firmware.FirmwareCatalogService above this facet.
    public interface IFirmwareRepository
    {
        /// Every catalog row, newest DateAdded first - callers sort by semver themselves (api.Firmware.FirmwareVersion) since the DB can't order "1.10.0" after "1.9.0".
        Task<IList<DeviceFirmware>> FirmwareListAsync();

        Task<DeviceFirmware?> FirmwareGetAsync(int idDeviceFirmware);

        /// Rows for one board across the given sources - see DeviceFirmware.Source for why the caller passes a set (the active source plus Local).
        Task<IList<DeviceFirmware>> FirmwareListForBoardAsync(string board, IReadOnlyCollection<FirmwareSource> sources);

        Task<int> FirmwareAddAsync(DeviceFirmware firmware);

        Task FirmwareDeleteAsync(int idDeviceFirmware);

        /// Removes every row a source created - a Refresh re-reads GitHub/Custom from scratch, a full Local rebuild wipes before re-pulling.
        Task<int> FirmwareDeleteBySourceAsync(FirmwareSource source);

        /// Atomic delete-then-repopulate for one source, rolled back on any mid-transaction failure. Returns how many old rows were removed.
        Task<int> FirmwareReplaceSourceRowsAsync(FirmwareSource source, IReadOnlyList<DeviceFirmware> rows);

        /// Arms (update=true, optional pinned version) or clears (update=false, null) the per-device OTA request - see api.Models.Device.FirmwareTargetVersion.
        Task DeviceFirmwareUpdateSetAsync(int idDevice, bool update, string? targetVersion);

        /// The board this device last reported in its heartbeat (deviceDiagnostic.Board), or null if it never has.
        Task<string?> DeviceBoardGetAsync(int idDevice);
    }
}
