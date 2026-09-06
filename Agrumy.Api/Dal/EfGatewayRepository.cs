using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IGatewayRepository, extracted out of the EfRepository god class (roadmap #246) - reads db.Devices directly rather than calling into IDeviceRepository, and reuses EfDeviceRepository.ToDto for the one DeviceRow-to-Device mapping it needs.
    internal sealed class EfGatewayRepository(AgrumyDbContext db) : IGatewayRepository
    {
        public async Task<IList<Device>> GatewayDevicesGetAllAsync()
        {
            // LoRaGatewayEnabled (roadmap #383) lists alongside the classic IsGateway (standalone Agrumy.Gateway) devices - both relay through the same GatewayApiController.Batch path.
            var rows = await db.Devices.AsNoTracking().Where(d => d.IsGateway || d.LoRaGatewayEnabled == true).ToListAsync();
            return rows.Select(EfDeviceRepository.ToDto).ToList();
        }

        public async Task<IList<GatewayDeviceMapping>> GatewayDeviceMappingsGetAsync(int idGatewayDevice) =>
            await MappingsQuery(idGatewayDevice, includeSecrets: false).ToListAsync();

        public async Task<IList<GatewayDeviceMapping>> GatewayDeviceMappingsWithSecretsGetAsync(int idGatewayDevice) =>
            await MappingsQuery(idGatewayDevice, includeSecrets: true).ToListAsync();

        private IQueryable<GatewayDeviceMapping> MappingsQuery(int idGatewayDevice, bool includeSecrets) =>
            from m in db.GatewayDeviceMappings.AsNoTracking()
            join dev in db.Devices.AsNoTracking() on m.IDDevice equals dev.IDDevice
            where m.IDGatewayDevice == idGatewayDevice
            select new GatewayDeviceMapping
            {
                IDGatewayDeviceMapping = m.IDGatewayDeviceMapping,
                IDGatewayDevice = m.IDGatewayDevice,
                DevEUI = m.DevEUI,
                IDDevice = m.IDDevice,
                DeviceName = dev.DeviceName,
                DeviceApiId = dev.ApiId,
                DeviceApiKey = includeSecrets ? dev.ApiKey : null,
                DateCreated = m.DateCreated,
            };

        public async Task<bool> GatewayDeviceMappingAddAsync(int idGatewayDevice, string devEUI, int idDevice, int gatewayTenantId)
        {
            // Unconditional, no caller-role exception (same reasoning as DeviceUnitApiController's Zone/Assign check) - a gateway must never be handed another tenant's device ApiKey, not even by a Global admin's mistake.
            if (!await db.Devices.AsNoTracking().AnyAsync(d => d.IDDevice == idDevice && d.TenantID == gatewayTenantId))
            {
                return false;
            }
            if (await db.GatewayDeviceMappings.AsNoTracking()
                .AnyAsync(m => m.IDGatewayDevice == idGatewayDevice && m.DevEUI == devEUI))
            {
                return false;
            }

            db.GatewayDeviceMappings.Add(new GatewayDeviceMappingRow
            {
                IDGatewayDevice = idGatewayDevice,
                DevEUI = devEUI,
                IDDevice = idDevice,
            });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> GatewayDeviceMappingDeleteAsync(int idGatewayDeviceMapping, int idGatewayDevice)
        {
            int rows = await db.GatewayDeviceMappings
                .Where(m => m.IDGatewayDeviceMapping == idGatewayDeviceMapping && m.IDGatewayDevice == idGatewayDevice)
                .ExecuteDeleteAsync();
            return rows > 0;
        }
    }
}
