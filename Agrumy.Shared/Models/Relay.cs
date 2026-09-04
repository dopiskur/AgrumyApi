using System.Text.Json;

namespace api.Models
{
    /// <summary>Agrumy.Relay is a device like any other (registers through the same
    /// PIN flow as api.Models.DeviceRegistration) but forwards OTHER devices' traffic instead of
    /// reporting its own sensors - Profile distinguishes which of the two mechanisms it runs.</summary>
    public enum RelayProfile
    {
        /// <summary>Local WiFi repeater: each device keeps its own ApiId/ApiKey, Relay is a
        /// transparent HTTP forwarder - no DevEUI mapping needed.</summary>
        WiFiRepeater = 0,
        /// <summary>LoRa gateway: uplinks arrive over ChirpStack MQTT keyed by DevEUI, which Relay
        /// maps to a device's ApiId/ApiKey via RelayDeviceMapping before forwarding.</summary>
        LoRaGateway = 1,
    }

    /// <summary>ServerConfig.RelayMode, Global-Admin-only. Realtime forwards each entry the moment
    /// it arrives (Profile A pass-through, or a Profile B downlink as soon as physically possible).
    /// Aggregated holds entries up to RelayWaitWindowSeconds before flushing as one batch - the
    /// mechanism a LoRa Class A device's decoupled uplink/downlink cycle actually needs; a live
    /// WiFi HTTP connection (Profile A) has no use for it since the caller is already blocked
    /// waiting on the socket.</summary>
    public enum RelayMode
    {
        Realtime = 0,
        Aggregated = 1,
    }

    /// <summary>What kind of device-facing call one api.Models.RelayBatchEntry carries - each maps
    /// to one of the existing DeviceApiController/SensorDataController actions, reused verbatim
    /// (not duplicated) by api.Controllers.API.RelayApiController.Batch.</summary>
    public enum RelayEntryType
    {
        Config = 0,
        SensorData = 1,
        Event = 2,
        CommandAck = 3,
    }

    /// <summary>One device's request, riding inside a Relay's batch instead of that device's own
    /// direct HTTPS call. DeviceApiId/DeviceApiKey are the SAME permanent credential the device
    /// would otherwise send as apiId/apiKey headers directly - Relay is trusted with them (Profile
    /// B's DevEUI mapping literally stores them), it does not mint or see anything new.</summary>
    public class RelayBatchEntry
    {
        public string? DeviceApiId { get; set; }
        public string? DeviceApiKey { get; set; }
        public RelayEntryType Type { get; set; }

        /// <summary>Deserialized against DeviceConfigPoll / a sensorData JsonArray / DeviceEventPush
        /// / CommandAckRequest depending on Type - one flat wire shape for every entry type instead
        /// of a payload field per type.</summary>
        public JsonElement Payload { get; set; }
    }

    public class RelayBatchRequest
    {
        public IList<RelayBatchEntry> Entries { get; set; } = [];
    }

    /// <summary>One entry's outcome, aligned 1:1 by index with the request's Entries - a batch
    /// partially failing (one device's ApiKey wrong, say) must not fail entries that were fine.</summary>
    public class RelayBatchEntryResult
    {
        public bool Success { get; set; }
        /// <summary>Mirrors the HTTP status the wrapped single-device endpoint would have returned
        /// (200/401/403/404/...) so Relay can translate it back to that device 1:1.</summary>
        public int StatusCode { get; set; }
        /// <summary>Present only for Type=Config - the DeviceConfig body a direct Config poll would
        /// have received. Null for every other type, and for Config when nothing changed (mirrors
        /// GetConfig's own empty-200 shortcut).</summary>
        public DeviceConfig? Config { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>Response of POST /api/Relay/Batch. Doubles as Relay's own config sync - RelayMode/
    /// RelayWaitWindowSeconds ride along on every batch response, same "the poll IS the heartbeat"
    /// pattern DeviceConfigPoll already uses, so Relay never needs a second call just to notice an
    /// admin changed its operating mode.</summary>
    public class RelayBatchResponse
    {
        public IList<RelayBatchEntryResult> Results { get; set; } = [];
        public RelayMode RelayMode { get; set; }
        public int RelayWaitWindowSeconds { get; set; }
    }

    /// <summary>Admin-managed row mapping a LoRaWAN end-device's DevEUI to the Agrumy device whose
    /// ApiId/ApiKey Relay should use on its behalf - only meaningful for RelayProfile.LoRaGateway.</summary>
    public class RelayDeviceMapping
    {
        public int? IDRelayDeviceMapping { get; set; }
        public int? IDRelayDevice { get; set; }
        /// <summary>16 hex chars, LoRaWAN's own device identifier - not an Agrumy-minted id.</summary>
        public string? DevEUI { get; set; }
        public int? IDDevice { get; set; }
        // Denormalized for the admin list view so it doesn't need a second round trip per row.
        public string? DeviceName { get; set; }
        public string? DeviceApiId { get; set; }
        // Only ever sent to the OWNING Relay (GET /api/Relay/DeviceMapping, ApiKeyPolicy) -
        // never to the admin-facing list endpoint. See RelayApiController.
        public string? DeviceApiKey { get; set; }
        public DateTime? DateCreated { get; set; }
    }
}
