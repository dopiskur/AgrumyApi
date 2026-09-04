using api.Models;

namespace api.Relay
{
    /// <summary>Bound from appsettings.json / environment variables (AgrumyService/Relay/ChirpStack
    /// sections) - see appsettings.json.example for the full set with comments.</summary>
    public class RelayOptions
    {
        public AgrumyServiceOptions AgrumyService { get; set; } = new();
        public RelaySelfOptions Relay { get; set; } = new();
        public ChirpStackOptions ChirpStack { get; set; } = new();
    }

    public class AgrumyServiceOptions
    {
        public string BaseUrl { get; set; } = "https://api.agrumy.com";
        /// <summary>Owning user's email - same identity a normal AgrumyFirmware device registers
        /// under (POST /api/User/DevicePin on that account is what produces DevicePin below).</summary>
        public string Email { get; set; } = "";
        public string DevicePin { get; set; } = "";
    }

    public class RelaySelfOptions
    {
        /// <summary>Any string unique to this physical relay - AgrumyFirmware devices use a real
        /// WiFi MAC here, but a relay (typically a Raspberry Pi) has no single canonical one; the
        /// registration DB column is just a uniqueness key, not parsed as a MAC anywhere.</summary>
        public string MacAddress { get; set; } = "";
        public RelayProfile Profile { get; set; } = RelayProfile.WiFiRepeater;
        /// <summary>Where registration persists ApiId/ApiKey/IDDevice after the one-time PIN
        /// registration succeeds, so a restart does not need the PIN again - same reason
        /// AgrumyFirmware persists deviceRegistration.json to LittleFS.</summary>
        public string RegistrationFilePath { get; set; } = "relay-registration.json";
        /// <summary>Profile A (WiFiRepeater) only - local devices point their ServicePoint at this
        /// relay's own address:port instead of AgrumyService directly.</summary>
        public int LocalPort { get; set; } = 5080;
    }

    /// <summary>Profile B (LoRaGateway) only - ChirpStack's own MQTT integration, untested against
    /// a real broker (see ChirpStackUplinkService's class remarks).</summary>
    public class ChirpStackOptions
    {
        public string MqttHost { get; set; } = "localhost";
        public int MqttPort { get; set; } = 1883;
        public string? MqttUsername { get; set; }
        public string? MqttPassword { get; set; }
        /// <summary>ChirpStack application id the uplink/downlink topics are namespaced under
        /// (application/{ApplicationId}/device/{devEui}/event/up, .../command/down).</summary>
        public string ApplicationId { get; set; } = "";
        /// <summary>How often to re-fetch this relay's DevEUI mapping from GET /api/Relay/DeviceMapping.</summary>
        public int MappingRefreshSeconds { get; set; } = 60;
    }
}
