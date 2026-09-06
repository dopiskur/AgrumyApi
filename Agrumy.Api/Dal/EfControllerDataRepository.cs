using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IControllerDataRepository, extracted out of the EfRepository god class (roadmap #246) - a leaf facet, no dependency on any other facet. See api.Dal.Entities.ControllerDataRow for why this upserts current state rather than appending a log.
    internal sealed class EfControllerDataRepository(AgrumyDbContext db) : IControllerDataRepository
    {
        public async Task ControllerDataPushAsync(int deviceID, int tenantID, IList<ControllerDataPush> entries)
        {
            foreach (ControllerDataPush entry in entries)
            {
                var row = await db.ControllerData.FirstOrDefaultAsync(c => c.DeviceID == deviceID && c.RelayFunction == (int)entry.RelayFunction);
                if (row == null)
                {
                    row = new ControllerDataRow { DeviceID = deviceID, RelayFunction = (int)entry.RelayFunction };
                    db.ControllerData.Add(row);
                }
                row.TenantID = tenantID;
                row.IsOn = entry.IsOn;
                row.DateChanged = entry.DateCreated ?? DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
        }

        public async Task<IList<ControllerDataStatus>> ControllerDataGetAsync(int deviceID)
        {
            return await db.ControllerData.AsNoTracking()
                .Where(c => c.DeviceID == deviceID)
                .Select(c => new ControllerDataStatus { RelayFunction = (RelayFunction)c.RelayFunction, IsOn = c.IsOn, DateChanged = c.DateChanged })
                .ToListAsync();
        }
    }
}
