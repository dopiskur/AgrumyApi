using api.Models;

namespace api.Gateway
{
    /// Bound from appsettings.json / environment variables (AgrumyService/Gateway/ChirpStack sections) - see appsettings.json.example for the full set with comments.
    public class GatewayOptions
    {
        public AgrumyServiceOptions AgrumyService { get; set; } = new();
        public GatewaySelfOptions Gateway { get; set; } = new();
        public ChirpStackOptions ChirpStack { get; set; } = new();
    }

    public class AgrumyServiceOptions
    {
        public string BaseUrl { get; set; } = "https://api.agrumy.com";
        /// Owning user's email - same identity a normal AgrumyFirmware device registers under (POST /api/User/DevicePin on that account produces DevicePin below).
        public string Email { get; set; } = "";
        public string DevicePin { get; set; } = "";
    }

    public class GatewaySelfOptions
    {
        /// Any string unique to this physical gateway - a Raspberry Pi has no single canonical MAC; the registration DB column is just a uniqueness key, not parsed as a MAC anywhere.
        public string MacAddress { get; set; } = "";
        public GatewayProfile Profile { get; set; } = GatewayProfile.WiFiRepeater;
        /// Where registration persists ApiId/ApiKey/IDDevice after the one-time PIN succeeds, so a restart doesn't need the PIN again - same reason AgrumyFirmware persists deviceRegistration.json to LittleFS.
        public string RegistrationFilePath { get; set; } = "gateway-registration.json";
        /// Must match the server's Gateway:RegistrationSecret - without it, Register still succeeds but silently drops IsGateway, so gateway-only endpoints stay unreachable for this device.
        public string RegistrationSecret { get; set; } = "";
        /// Profile A (WiFiRepeater) only - local devices point their ServicePoint at this gateway's own address:port instead of AgrumyService directly.
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
        /// How often to re-fetch this gateway's DevEUI mapping from GET /api/Gateway/DeviceMapping.
        public int MappingRefreshSeconds { get; set; } = 60;
    }
}
