using System.Text.Json;
using Microsoft.Extensions.Options;

namespace api.Gateway.Registration
{
    /// In-memory holder for this gateway's own ApiId/ApiKey/IDDevice, backed by a JSON file on disk - registered as a singleton so every handler/background service sees the same state without re-reading the file.
    public class GatewayRegistrationStore(IOptions<GatewayOptions> options)
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly string path = options.Value.Gateway.RegistrationFilePath;
        private GatewayRegistrationState state = new();
        private readonly object gate = new();

        public GatewayRegistrationState Current
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
            var loaded = JsonSerializer.Deserialize<GatewayRegistrationState>(json);
            if (loaded != null)
            {
                lock (gate) { state = loaded; }
            }
        }

        /// Temp file + rename (same pattern as FirmwareStorage) so a crash/power-loss mid-write never leaves a half-written, unparseable registration file.
        public void Save(GatewayRegistrationState newState)
        {
            lock (gate) { state = newState; }
            string json = JsonSerializer.Serialize(newState, WriteOptions);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
