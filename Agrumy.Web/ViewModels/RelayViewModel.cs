using api.Models;

namespace api.ViewModels
{
    public class RelayListViewModel
    {
        public required IList<DeviceDto> Relays { get; init; }
    }

    public class RelayMappingViewModel
    {
        public required DeviceDto Relay { get; init; }
        public required IList<RelayDeviceMapping> Mappings { get; init; }
        // Every non-relay device, for the "map this DevEUI to..." picker - a relay-to-relay mapping would make no sense.
        public required IList<DeviceDto> AvailableDevices { get; init; }
    }
}
