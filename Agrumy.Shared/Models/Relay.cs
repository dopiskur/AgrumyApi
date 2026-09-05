using System.Text.Json;

namespace api.Models
{
    /// Agrumy.Relay registers through the same PIN flow as DeviceRegistration but forwards other devices' traffic instead of reporting its own sensors - Profile picks which mechanism it runs.
    public enum RelayProfile
    {
        /// Local WiFi repeater: each device keeps its own ApiId/ApiKey, Relay is a transparent HTTP forwarder - no DevEUI mapping needed.
        WiFiRepeater = 0,
        /// LoRa gateway: uplinks arrive over ChirpStack MQTT keyed by DevEUI, which Relay maps to a device's ApiId/ApiKey via RelayDeviceMapping before forwarding.
        LoRaGateway = 1,
    }

    /// ServerConfig.RelayMode, Global-Admin-only. Realtime forwards each entry immediately; Aggregated holds entries up to RelayWaitWindowSeconds for a LoRa Class A device's decoupled uplink/downlink cycle - a live WiFi HTTP connection has no use for it since the caller is already blocked on the socket.
    public enum RelayMode
    {
        Realtime = 0,
        Aggregated = 1,
    }

    /// Which device-facing call one RelayBatchEntry carries - each maps to an existing DeviceApiController/SensorDataController action, reused verbatim by RelayApiController.Batch.
    public enum RelayEntryType
    {
        Config = 0,
        SensorData = 1,
        Event = 2,
        CommandAck = 3,
    }

    /// One device's request riding inside a Relay's batch instead of a direct HTTPS call; DeviceApiId/DeviceApiKey are the same permanent credential the device would send itself - Relay is trusted with them, not minting anything new.
    public class RelayBatchEntry
    {
        public string? DeviceApiId { get; set; }
        public string? DeviceApiKey { get; set; }
        public RelayEntryType Type { get; set; }

        /// Deserialized against DeviceConfigPoll / a sensorData JsonArray / DeviceEventPush / CommandAckRequest depending on Type - one flat wire shape instead of a payload field per type.
        public JsonElement Payload { get; set; }
    }

    public class RelayBatchRequest
    {
        public IList<RelayBatchEntry> Entries { get; set; } = [];
    }

    /// One entry's outcome, aligned 1:1 by index with the request's Entries - a batch partially failing (one device's ApiKey wrong, say) must not fail entries that were fine.
    public class RelayBatchEntryResult
    {
        public bool Success { get; set; }
        /// Mirrors the HTTP status the wrapped single-device endpoint would have returned so Relay can translate it back to that device 1:1.
        public int StatusCode { get; set; }
        /// Present only for Type=Config - the DeviceConfig body a direct poll would receive; null for every other type, and for Config when nothing changed (mirrors GetConfig's empty-200 shortcut).
        public DeviceConfig? Config { get; set; }
        public string? Error { get; set; }
    }

    /// Response of POST /api/Relay/Batch, doubling as Relay's own config sync - RelayMode/RelayWaitWindowSeconds ride along on every response so Relay never needs a second call to notice its operating mode changed.
    public class RelayBatchResponse
    {
        public IList<RelayBatchEntryResult> Results { get; set; } = [];
        public RelayMode RelayMode { get; set; }
        public int RelayWaitWindowSeconds { get; set; }
    }

    /// Admin-managed row mapping a LoRaWAN end-device's DevEUI to the Agrumy device whose ApiId/ApiKey Relay should use on its behalf - only meaningful for RelayProfile.LoRaGateway.
    public class RelayDeviceMapping
    {
        public int? IDRelayDeviceMapping { get; set; }
        public int? IDRelayDevice { get; set; }
        /// 16 hex chars, LoRaWAN's own device identifier - not an Agrumy-minted id.
        public string? DevEUI { get; set; }
        public int? IDDevice { get; set; }
        // Denormalized for the admin list view so it doesn't need a second round trip per row.
        public string? DeviceName { get; set; }
        public string? DeviceApiId { get; set; }
        // Only ever sent to the OWNING Relay (GET /api/Relay/DeviceMapping, ApiKeyPolicy), never to the admin-facing list endpoint.
        public string? DeviceApiKey { get; set; }
        public DateTime? DateCreated { get; set; }
    }
}
