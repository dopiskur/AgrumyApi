using api.Models;

namespace api.Dal.Interface
{
    /// DevEUI&lt;-&gt;device mapping and relay listing for Agrumy.Relay's LoRaGateway profile and its admin UI - relay device rows themselves live in the ordinary device table (IDeviceRepository).
    public interface IRelayRepository
    {
        /// Every device row with IsRelay=true, across every tenant - relays are install-wide infrastructure, not tenant data.
        Task<IList<Device>> RelayDevicesGetAllAsync();

        /// One relay's DevEUI mappings, without the mapped device's ApiKey - the admin list view, not what Relay itself fetches to build its forwarding cache.
        Task<IList<RelayDeviceMapping>> RelayDeviceMappingsGetAsync(int idRelayDevice);

        /// Same rows as above, WITH each mapped device's ApiKey - only ever served to the owning relay itself, never to the admin UI.
        Task<IList<RelayDeviceMapping>> RelayDeviceMappingsWithSecretsGetAsync(int idRelayDevice);

        /// False (no-op) if idDevice doesn't exist or DevEUI is already mapped for this relay - the unique index is the real guard, this is just a friendlier failure than a raw constraint exception.
        Task<bool> RelayDeviceMappingAddAsync(int idRelayDevice, string devEUI, int idDevice);

        Task<bool> RelayDeviceMappingDeleteAsync(int idRelayDeviceMapping, int idRelayDevice);
    }
}
