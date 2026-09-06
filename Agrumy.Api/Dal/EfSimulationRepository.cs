using api.Dal.Entities;
using api.Dal.Interface;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// ISimulationRepository, extracted out of the EfRepository god class (roadmap #246) - the virtual-device registry. VirtualDeviceDeleteAsync needs IDeviceRepository (delegates the actual device-row delete to it), an already-extracted facet, so no circular dependency.
    internal sealed class EfSimulationRepository(AgrumyDbContext db, IDeviceRepository deviceRepository) : ISimulationRepository
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
            await deviceRepository.DeviceDeleteAsync(deviceID, tenantID);
        }
    }
}
