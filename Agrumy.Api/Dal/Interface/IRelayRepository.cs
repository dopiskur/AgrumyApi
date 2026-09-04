using api.Models;

namespace api.Dal.Interface
{
    /// <summary>DevEUI&lt;-&gt;device mapping and relay listing for Agrumy.Relay
    /// (RelayProfile.LoRaGateway) and its admin UI. Relay device rows themselves live in the
    /// ordinary device table (IDeviceRepository) - this facet only covers what's specific to a
    /// relay: the mapping table and filtering the fleet down to relays.</summary>
    public interface IRelayRepository
    {
        /// <summary>Every device row with IsRelay=true, across every tenant - relays are
        /// install-wide infrastructure, not tenant data, same reasoning as
        /// DevicesGetAllAsync/UsersGetAllAsync for other install-wide listings.</summary>
        Task<IList<Device>> RelayDevicesGetAllAsync();

        /// <summary>One relay's DevEUI mappings, without the mapped device's ApiKey - the admin
        /// list view, not what Relay itself fetches to build its forwarding cache.</summary>
        Task<IList<RelayDeviceMapping>> RelayDeviceMappingsGetAsync(int idRelayDevice);

        /// <summary>Same rows as above, WITH each mapped device's ApiKey - only ever served to the
        /// owning relay itself (RelayApiController's ApiKeyPolicy-authorized GET), never to the
        /// admin UI.</summary>
        Task<IList<RelayDeviceMapping>> RelayDeviceMappingsWithSecretsGetAsync(int idRelayDevice);

        /// <summary>False (no-op) if idDevice does not exist or DevEUI is already mapped for this
        /// relay - the unique index is the real guard, this is just a friendlier failure than a
        /// raw constraint-violation exception reaching the controller.</summary>
        Task<bool> RelayDeviceMappingAddAsync(int idRelayDevice, string devEUI, int idDevice);

        Task<bool> RelayDeviceMappingDeleteAsync(int idRelayDeviceMapping, int idRelayDevice);
    }
}
