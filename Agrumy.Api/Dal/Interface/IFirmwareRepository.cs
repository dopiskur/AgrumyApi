using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Firmware catalog facet of the data layer: raw deviceFirmware rows and the per-device
    /// update flags only - source selection, semver ordering, downloading and file storage all live
    /// in api.Firmware.FirmwareCatalogService, which sits above this facet the same way
    /// CommandQueueService sits above ICommandRepository. The legacy board-less
    /// DeviceFirmwareLatestGetAsync(deviceTypeID) lookup stays on IDeviceRepository, untouched.</summary>
    public interface IFirmwareRepository
    {
        /// <summary>Every catalog row, newest DateAdded first - callers sort by semver themselves
        /// (api.Firmware.FirmwareVersion), the DB cannot order "1.10.0" after "1.9.0".</summary>
        Task<IList<DeviceFirmware>> FirmwareListAsync();

        Task<DeviceFirmware?> FirmwareGetAsync(int idDeviceFirmware);

        /// <summary>Rows for one board across the given sources (see DeviceFirmware.Source for why
        /// the caller passes a set - the active source plus Local).</summary>
        Task<IList<DeviceFirmware>> FirmwareListForBoardAsync(string board, IReadOnlyCollection<FirmwareSource> sources);

        Task<int> FirmwareAddAsync(DeviceFirmware firmware);

        Task FirmwareDeleteAsync(int idDeviceFirmware);

        /// <summary>Removes every row a source created - a Refresh re-reads GitHub/Custom from
        /// scratch, a full Local rebuild wipes before re-pulling. Returns how many went.</summary>
        Task<int> FirmwareDeleteBySourceAsync(FirmwareSource source);

        /// <summary>Atomic delete-then-repopulate for one source, rolled back on any mid-transaction failure. Returns how many old rows were removed.</summary>
        Task<int> FirmwareReplaceSourceRowsAsync(FirmwareSource source, IReadOnlyList<DeviceFirmware> rows);

        /// <summary>Arms (update=true, optional pinned version) or clears (update=false, null) the
        /// per-device OTA request - see api.Models.Device.FirmwareTargetVersion.</summary>
        Task DeviceFirmwareUpdateSetAsync(int idDevice, bool update, string? targetVersion);

        /// <summary>The board this device last reported in its heartbeat (deviceDiagnostic.Board),
        /// or null if it never has.</summary>
        Task<string?> DeviceBoardGetAsync(int idDevice);
    }
}
