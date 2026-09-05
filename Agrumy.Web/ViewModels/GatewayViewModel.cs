using api.Models;

namespace api.ViewModels
{
    public class GatewayListViewModel
    {
        public required IList<DeviceDto> Gateways { get; init; }
    }

    public class GatewayMappingViewModel
    {
        public required DeviceDto Gateway { get; init; }
        public required IList<GatewayDeviceMapping> Mappings { get; init; }
        // Every non-gateway device, for the "map this DevEUI to..." picker - a gateway-to-gateway mapping would make no sense.
        public required IList<DeviceDto> AvailableDevices { get; init; }
    }
}
