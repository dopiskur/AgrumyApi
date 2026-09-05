using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IDeviceRepository members: legacy OTA lookup, the fallback for a device whose firmware never reported a Board - the board-keyed catalog lives in EfRepository.Firmware.cs.
    internal partial class EfRepository
    {
        public async Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID)
        {
            var row = await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.DeviceTypeID == deviceTypeID)
                .OrderByDescending(f => f.DateAdded)
                .FirstOrDefaultAsync();
            return row == null ? null : FirmwareToDto(row);
        }
    }
}
