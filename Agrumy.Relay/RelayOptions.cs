using api.Models;

namespace api.Relay
{
    /// Bound from appsettings.json / environment variables (AgrumyService/Relay/ChirpStack sections) - see appsettings.json.example for the full set with comments.
    public class RelayOptions
    {
        public AgrumyServiceOptions AgrumyService { get; set; } = new();
        public RelaySelfOptions Relay { get; set; } = new();
        public ChirpStackOptions ChirpStack { get; set; } = new();
    }

    public class AgrumyServiceOptions
    {
        public string BaseUrl { get; set; } = "https://api.agrumy.com";
        /// Owning user's email - same identity a normal AgrumyFirmware device registers under (POST /api/User/DevicePin on that account produces DevicePin below).
        public string Email { get; set; } = "";
        public string DevicePin { get; set; } = "";
    }

    public class RelaySelfOptions
    {
        /// Any string unique to this physical relay - a Raspberry Pi has no single canonical MAC; the registration DB column is just a uniqueness key, not parsed as a MAC anywhere.
        public string MacAddress { get; set; } = "";
        public RelayProfile Profile { get; set; } = RelayProfile.WiFiRepeater;
        /// Where registration persists ApiId/ApiKey/IDDevice after the one-time PIN succeeds, so a restart doesn't need the PIN again - same reason AgrumyFirmware persists deviceRegistration.json to LittleFS.
        public string RegistrationFilePath { get; set; } = "relay-registration.json";
        /// Profile A (WiFiRepeater) only - local devices point their ServicePoint at this relay's own address:port instead of AgrumyService directly.
        public int LocalPort { get; set; } = 5080;
    }

    /// Profile B (LoRaGateway) only - ChirpStack's own MQTT integration, untested against a real broker (see ChirpStackUplinkService's remarks).
    public class ChirpStackOptions
    {
        public string MqttHost { get; set; } = "localhost";
        public int MqttPort { get; set; } = 1883;
        public string? MqttUsername { get; set; }
        public string? MqttPassword { get; set; }
        /// ChirpStack application id the uplink/downlink topics are namespaced under (application/{ApplicationId}/device/{devEui}/event/up, .../command/down).
        public string ApplicationId { get; set; } = "";
        /// How often to re-fetch this relay's DevEUI mapping from GET /api/Relay/DeviceMapping.
        public int MappingRefreshSeconds { get; set; } = 60;
    }
}
