using System.Text.Json;
using Microsoft.Extensions.Options;

namespace api.Relay.Registration
{
    /// <summary>In-memory holder for this relay's own ApiId/ApiKey/IDDevice, backed by a JSON file
    /// on disk (RelaySelfOptions.RegistrationFilePath) - registered as a singleton so every request
    /// handler and background service sees the same state without re-reading the file each time.</summary>
    public class RelayRegistrationStore(IOptions<RelayOptions> options)
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly string path = options.Value.Relay.RegistrationFilePath;
        private RelayRegistrationState state = new();
        private readonly object gate = new();

        public RelayRegistrationState Current
        {
            get { lock (gate) { return state; } }
        }

        public void Load()
        {
            if (!File.Exists(path))
            {
                return;
            }
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<RelayRegistrationState>(json);
            if (loaded != null)
            {
                lock (gate) { state = loaded; }
            }
        }

        public void Save(RelayRegistrationState newState)
        {
            lock (gate) { state = newState; }
            string json = JsonSerializer.Serialize(newState, WriteOptions);
            File.WriteAllText(path, json);
        }
    }
}
