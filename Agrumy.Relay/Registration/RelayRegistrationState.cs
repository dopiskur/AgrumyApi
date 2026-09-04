namespace api.Relay.Registration
{
    /// <summary>What gets persisted to RelaySelfOptions.RegistrationFilePath once
    /// POST /api/Device/Register succeeds - the same three values AgrumyFirmware keeps in its own
    /// deviceRegistration.json, just serialized as plain JSON here instead of LittleFS.</summary>
    public class RelayRegistrationState
    {
        public string? ApiId { get; set; }
        public string? ApiKey { get; set; }
        public int? IdDevice { get; set; }

        public bool IsComplete => !string.IsNullOrEmpty(ApiId) && !string.IsNullOrEmpty(ApiKey);
    }
}
