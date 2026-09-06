using api.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// ISimulationRepository members - the virtual-device registry.
    internal partial class EfRepository
    {
        public async Task VirtualDeviceRegisterAsync(int deviceID)
        {
            db.DeviceVirtuals.Add(new DeviceVirtualRow { DeviceID = deviceID, DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        public async Task<IList<int>> VirtualDeviceIdsGetAsync() =>
            await db.DeviceVirtuals.AsNoTracking().Select(v => v.DeviceID).ToListAsync();

        public async Task<IList<int>> VirtualDeviceIdsGetAsync(int? tenantID)
        {
            IQueryable<int> ids = db.DeviceVirtuals.AsNoTracking()
                .Join(db.Devices.AsNoTracking(), v => v.DeviceID, d => d.IDDevice, (v, d) => new { v.DeviceID, d.TenantID })
                .Where(x => tenantID == null || x.TenantID == tenantID)
                .Select(x => x.DeviceID);
            return await ids.ToListAsync();
        }

        public async Task VirtualDeviceDeleteAsync(int deviceID, int tenantID)
        {
            // Synthetic telemetry has no historical value once the device is gone - unlike DeviceDeleteAsync's rule for a REAL device, whose sensorData stays for the record.
            await db.SensorData.Where(s => s.DeviceID == deviceID).ExecuteDeleteAsync();
            await db.DeviceVirtuals.Where(v => v.DeviceID == deviceID).ExecuteDeleteAsync();
            await DeviceDeleteAsync(deviceID, tenantID);
        }
    }
}
