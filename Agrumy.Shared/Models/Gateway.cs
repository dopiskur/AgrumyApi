using System.Text.Json;

namespace api.Models
{
    /// Agrumy.Gateway registers through the same PIN flow as DeviceRegistration but forwards other devices' traffic instead of reporting its own sensors - Profile picks which mechanism it runs.
    public enum GatewayProfile
    {
        /// Local WiFi repeater: each device keeps its own ApiId/ApiKey, Gateway is a transparent HTTP forwarder - no DevEUI mapping needed.
        WiFiRepeater = 0,
        /// LoRa gateway: uplinks arrive over ChirpStack MQTT keyed by DevEUI, which Gateway maps to a device's ApiId/ApiKey via GatewayDeviceMapping before forwarding.
        LoRaGateway = 1,
        /// LoRa gateway, private (non-LoRaWAN) protocol: uplinks arrive over a serial-attached RadioLib radio-frontend board keyed by a 16-bit node address, mapped the same way as LoRaGateway's DevEUI (GatewayDeviceMapping.DevEUI holds the address as a string here) - no ChirpStack/network-server dependency, see api.Gateway.LoRaPrivate.LoRaPrivateProtocolUplinkService.
        LoRaPrivateProtocol = 2,
    }

    /// ServerConfig.GatewayMode, Global-Admin-only. Realtime forwards each entry immediately; Aggregated holds entries up to GatewayWaitWindowSeconds for a LoRa Class A device's decoupled uplink/downlink cycle - a live WiFi HTTP connection has no use for it since the caller is already blocked on the socket.
    public enum GatewayMode
    {
        Realtime = 0,
        Aggregated = 1,
    }

    /// Which device-facing call one GatewayBatchEntry carries - each maps to an existing DeviceApiController/SensorDataController action, reused verbatim by GatewayApiController.Batch.
    public enum GatewayEntryType
    {
        Config = 0,
        SensorData = 1,
        Event = 2,
        CommandAck = 3,
    }

    /// One device's request riding inside a Gateway's batch instead of a direct HTTPS call; DeviceApiId/DeviceApiKey are the same permanent credential the device would send itself - Gateway is trusted with them, not minting anything new.
    public class GatewayBatchEntry
    {
        public string? DeviceApiId { get; set; }
        public string? DeviceApiKey { get; set; }
        public GatewayEntryType Type { get; set; }

        /// Deserialized against DeviceConfigPoll / a sensorData JsonArray / DeviceEventPush / CommandAckRequest depending on Type - one flat wire shape instead of a payload field per type.
        public JsonElement Payload { get; set; }
    }

    public class GatewayBatchRequest
    {
        public IList<GatewayBatchEntry> Entries { get; set; } = [];
    }

    /// One entry's outcome, aligned 1:1 by index with the request's Entries - a batch partially failing (one device's ApiKey wrong, say) must not fail entries that were fine.
    public class GatewayBatchEntryResult
    {
        public bool Success { get; set; }
        /// Mirrors the HTTP status the wrapped single-device endpoint would have returned so Gateway can translate it back to that device 1:1.
        public int StatusCode { get; set; }
        /// Present only for Type=Config - the DeviceConfig body a direct poll would receive; null for every other type, and for Config when nothing changed (mirrors GetConfig's empty-200 shortcut).
        public DeviceConfig? Config { get; set; }
        public string? Error { get; set; }
    }

    /// Response of POST /api/Gateway/Batch, doubling as Gateway's own config sync - GatewayMode/GatewayWaitWindowSeconds ride along on every response so Gateway never needs a second call to notice its operating mode changed.
    public class GatewayBatchResponse
    {
        public IList<GatewayBatchEntryResult> Results { get; set; } = [];
        public GatewayMode GatewayMode { get; set; }
        public int GatewayWaitWindowSeconds { get; set; }
    }

    /// Admin-managed row mapping a LoRaWAN end-device's DevEUI to the Agrumy device whose ApiId/ApiKey Gateway should use on its behalf - only meaningful for GatewayProfile.LoRaGateway.
    public class GatewayDeviceMapping
    {
        public int? IDGatewayDeviceMapping { get; set; }
        public int? IDGatewayDevice { get; set; }
        /// 16 hex chars, LoRaWAN's own device identifier - not an Agrumy-minted id.
        public string? DevEUI { get; set; }
        public int? IDDevice { get; set; }
        // Denormalized for the admin list view so it doesn't need a second round trip per row.
        public string? DeviceName { get; set; }
        public string? DeviceApiId { get; set; }
        // Only ever sent to the OWNING Gateway (GET /api/Gateway/DeviceMapping, ApiKeyPolicy), never to the admin-facing list endpoint.
        public string? DeviceApiKey { get; set; }
        public DateTime? DateCreated { get; set; }
    }

    /// Body of POST /api/Gateway/RelayUplink (roadmap #383) - a WiFi-connected LoRaGatewayEnabled device forwards one raw, already RF-decoded private-protocol frame, letting the server do the SAME address->device resolution + envelope dispatch api.Gateway.LoRaPrivate.LoRaPrivateProtocolUplinkService does for the serial-bridge path (GatewayDeviceMapping.DevEUI holds the address as a string here too) - "gateway is a transparent forwarder", same principle as Batch/LoRaGatewayBridgeController.
    public class GatewayRelayUplinkRequest
    {
        public ushort SourceAddress { get; set; }
        /// The frame's decoded payload text - a JSON envelope like {"t":"sensor","d":[...]} exactly as the sensor node built it (Logic/LoRaPayloadLogic), untouched by the relaying gateway.
        public string Payload { get; set; } = "";
    }
}
