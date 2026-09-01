using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceRepository members (roadmap #95 split, continuing #74): OTA firmware catalog
    /// lookups (roadmap #3).</summary>
    internal partial class EfRepository
    {
        public async Task<DeviceFirmware?> DeviceFirmwareLatestGetAsync(int? deviceTypeID)
        {
            return await db.DeviceFirmwares.AsNoTracking()
                .Where(f => f.DeviceTypeID == deviceTypeID)
                .OrderByDescending(f => f.DateAdded)
                .Select(f => new DeviceFirmware
                {
                    IDDeviceFirmware = f.IDDeviceFirmware,
                    DeviceTypeID = f.DeviceTypeID,
                    Version = f.Version,
                    Url = f.Url,
                    DateAdded = f.DateAdded,
                })
                .FirstOrDefaultAsync();
        }
    }
}
