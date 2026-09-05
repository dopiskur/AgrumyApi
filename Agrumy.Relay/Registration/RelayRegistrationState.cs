namespace api.Relay.Registration
{
    /// What gets persisted to RelaySelfOptions.RegistrationFilePath once POST /api/Device/Register succeeds - the same three values AgrumyFirmware keeps in deviceRegistration.json, just plain JSON instead of LittleFS.
    public class RelayRegistrationState
    {
        public string? ApiId { get; set; }
        public string? ApiKey { get; set; }
        public int? IdDevice { get; set; }

        public bool IsComplete => !string.IsNullOrEmpty(ApiId) && !string.IsNullOrEmpty(ApiKey);
    }
}
