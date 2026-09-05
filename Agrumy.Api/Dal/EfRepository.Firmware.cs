using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IFirmwareRepository members - the catalog rows themselves; the legacy board-less DeviceFirmwareLatestGetAsync lookup lives in EfRepository.Devices.Firmware.cs.
    internal partial class EfRepository
    {
        public async Task<IList<DeviceFirmware>> FirmwareListAsync()
        {
            var rows = await db.DeviceFirmwares.AsNoTracking()
                .OrderByDescending(f => f.DateAdded)
                .ToListAsync();
            return rows.Select(FirmwareToDto).ToList();
        }

        public async Task<DeviceFirmware?> FirmwareGetAsync(int idDeviceFirmware)
        {
            var row = await db.DeviceFirmwares.AsNoTracking().FirstOrDefaultAsync(f => f.IDDeviceFirmware == idDeviceFirmware);
            return row == null ? null : FirmwareToDto(row);
        }

        public async Task<IList<DeviceFirmware>> FirmwareListForBoardAsync(string board, IReadOnlyCollection<FirmwareSource> sources)
        {
            var sourceInts = sources.Select(s => (int)s).ToList();
            var rows = await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.Board == board && sourceInts.Contains(f.Source))
                .ToListAsync();
            return rows.Select(FirmwareToDto).ToList();
        }

        public async Task<int> FirmwareAddAsync(DeviceFirmware firmware)
        {
            var row = new DeviceFirmwareRow
            {
                DeviceTypeID = firmware.DeviceTypeID,
                Board = firmware.Board,
                Version = firmware.Version,
                Url = firmware.Url,
                Source = (int)firmware.Source,
                FileName = firmware.FileName,
                SizeBytes = firmware.SizeBytes,
                Sha256 = firmware.Sha256,
                PublishedAt = firmware.PublishedAt,
                DateAdded = DateTime.UtcNow,
                FullImageFileName = firmware.FullImageFileName,
                FullImageUrl = firmware.FullImageUrl,
                FullImageSizeBytes = firmware.FullImageSizeBytes,
                FullImageSha256 = firmware.FullImageSha256,
            };
            db.DeviceFirmwares.Add(row);
            await db.SaveChangesAsync();
            return row.IDDeviceFirmware;
        }

        public async Task FirmwareDeleteAsync(int idDeviceFirmware)
        {
            await db.DeviceFirmwares.Where(f => f.IDDeviceFirmware == idDeviceFirmware).ExecuteDeleteAsync();
        }

        public async Task<int> FirmwareDeleteBySourceAsync(FirmwareSource source)
        {
            int s = (int)source;
            // Legacy hand-inserted rows (null Board) are never swept by a source refresh - nothing here knows how to recreate them.
            return await db.DeviceFirmwares.Where(f => f.Source == s && f.Board != null).ExecuteDeleteAsync();
        }

        public async Task<int> FirmwareReplaceSourceRowsAsync(FirmwareSource source, IReadOnlyList<DeviceFirmware> rows)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            int s = (int)source;
            int removed = await db.DeviceFirmwares.Where(f => f.Source == s && f.Board != null).ExecuteDeleteAsync();

            db.DeviceFirmwares.AddRange(rows.Select(firmware => new DeviceFirmwareRow
            {
                DeviceTypeID = firmware.DeviceTypeID,
                Board = firmware.Board,
                Version = firmware.Version,
                Url = firmware.Url,
                Source = (int)firmware.Source,
                FileName = firmware.FileName,
                SizeBytes = firmware.SizeBytes,
                Sha256 = firmware.Sha256,
                PublishedAt = firmware.PublishedAt,
                DateAdded = DateTime.UtcNow,
                FullImageFileName = firmware.FullImageFileName,
                FullImageUrl = firmware.FullImageUrl,
                FullImageSizeBytes = firmware.FullImageSizeBytes,
                FullImageSha256 = firmware.FullImageSha256,
            }));
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
            return removed;
        }

        public async Task DeviceFirmwareUpdateSetAsync(int idDevice, bool update, string? targetVersion)
        {
            var row = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (row == null)
            {
                return;
            }
            row.FirmwareUpdate = update;
            row.FirmwareTargetVersion = update ? targetVersion : null;
            // Without this, GetConfig's "value.ConfigVersion == device.ConfigVersion && pendingCommand == null" short-circuit means an already-synced device never sees the flag flip until something else happens to bump ConfigVersion first.
            row.ConfigVersion = (row.ConfigVersion ?? 0) + 1;
            await db.SaveChangesAsync();
        }

        public async Task<string?> DeviceBoardGetAsync(int idDevice)
        {
            return await db.DeviceDiagnostics.AsNoTracking()
                .Where(d => d.DeviceID == idDevice)
                .Select(d => d.Board)
                .FirstOrDefaultAsync();
        }

        private static DeviceFirmware FirmwareToDto(DeviceFirmwareRow f) => new()
        {
            IDDeviceFirmware = f.IDDeviceFirmware,
            DeviceTypeID = f.DeviceTypeID,
            Board = f.Board,
            Version = f.Version,
            Url = f.Url,
            Source = (FirmwareSource)f.Source,
            FileName = f.FileName,
            SizeBytes = f.SizeBytes,
            Sha256 = f.Sha256,
            PublishedAt = f.PublishedAt,
            DateAdded = f.DateAdded,
            FullImageFileName = f.FullImageFileName,
            FullImageUrl = f.FullImageUrl,
            FullImageSizeBytes = f.FullImageSizeBytes,
            FullImageSha256 = f.FullImageSha256,
        };
    }
}
