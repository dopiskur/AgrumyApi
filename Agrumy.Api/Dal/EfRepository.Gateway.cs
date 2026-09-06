using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IGatewayRepository members - forwarded to the standalone EfGatewayRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<IList<Device>> GatewayDevicesGetAllAsync() => gatewayRepository.GatewayDevicesGetAllAsync();

        public Task<IList<GatewayDeviceMapping>> GatewayDeviceMappingsGetAsync(int idGatewayDevice) => gatewayRepository.GatewayDeviceMappingsGetAsync(idGatewayDevice);

        public Task<IList<GatewayDeviceMapping>> GatewayDeviceMappingsWithSecretsGetAsync(int idGatewayDevice) => gatewayRepository.GatewayDeviceMappingsWithSecretsGetAsync(idGatewayDevice);

        public Task<bool> GatewayDeviceMappingAddAsync(int idGatewayDevice, string devEUI, int idDevice, int gatewayTenantId) =>
            gatewayRepository.GatewayDeviceMappingAddAsync(idGatewayDevice, devEUI, idDevice, gatewayTenantId);

        public Task<bool> GatewayDeviceMappingDeleteAsync(int idGatewayDeviceMapping, int idGatewayDevice) =>
            gatewayRepository.GatewayDeviceMappingDeleteAsync(idGatewayDeviceMapping, idGatewayDevice);
    }
}
