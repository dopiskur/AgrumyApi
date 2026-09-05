using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IRelayRepository members.
    internal partial class EfRepository
    {
        public async Task<IList<Device>> RelayDevicesGetAllAsync()
        {
            var rows = await db.Devices.AsNoTracking().Where(d => d.IsRelay).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<IList<RelayDeviceMapping>> RelayDeviceMappingsGetAsync(int idRelayDevice) =>
            await MappingsQuery(idRelayDevice, includeSecrets: false).ToListAsync();

        public async Task<IList<RelayDeviceMapping>> RelayDeviceMappingsWithSecretsGetAsync(int idRelayDevice) =>
            await MappingsQuery(idRelayDevice, includeSecrets: true).ToListAsync();

        private IQueryable<RelayDeviceMapping> MappingsQuery(int idRelayDevice, bool includeSecrets) =>
            from m in db.RelayDeviceMappings.AsNoTracking()
            join dev in db.Devices.AsNoTracking() on m.IDDevice equals dev.IDDevice
            where m.IDRelayDevice == idRelayDevice
            select new RelayDeviceMapping
            {
                IDRelayDeviceMapping = m.IDRelayDeviceMapping,
                IDRelayDevice = m.IDRelayDevice,
                DevEUI = m.DevEUI,
                IDDevice = m.IDDevice,
                DeviceName = dev.DeviceName,
                DeviceApiId = dev.ApiId,
                DeviceApiKey = includeSecrets ? dev.ApiKey : null,
                DateCreated = m.DateCreated,
            };

        public async Task<bool> RelayDeviceMappingAddAsync(int idRelayDevice, string devEUI, int idDevice)
        {
            if (!await db.Devices.AsNoTracking().AnyAsync(d => d.IDDevice == idDevice))
            {
                return false;
            }
            if (await db.RelayDeviceMappings.AsNoTracking()
                .AnyAsync(m => m.IDRelayDevice == idRelayDevice && m.DevEUI == devEUI))
            {
                return false;
            }

            db.RelayDeviceMappings.Add(new RelayDeviceMappingRow
            {
                IDRelayDevice = idRelayDevice,
                DevEUI = devEUI,
                IDDevice = idDevice,
            });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RelayDeviceMappingDeleteAsync(int idRelayDeviceMapping, int idRelayDevice)
        {
            int rows = await db.RelayDeviceMappings
                .Where(m => m.IDRelayDeviceMapping == idRelayDeviceMapping && m.IDRelayDevice == idRelayDevice)
                .ExecuteDeleteAsync();
            return rows > 0;
        }
    }
}
