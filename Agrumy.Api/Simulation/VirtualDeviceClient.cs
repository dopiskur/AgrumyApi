using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using api.Models;

namespace api.Simulation
{
    /// Drives one virtual device through the EXACT same HTTP wire protocol a real AgrumyFirmware device uses (Register -> Authenticate -> Config -> SensorData -> ControllerData), against the server's own public address - the device-facing endpoints have no idea the caller isn't real hardware.
    public class VirtualDeviceClient(HttpClient http)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<DeviceConfig?> RegisterAsync(string macAddress, string email, string devicePin, string? displayName)
        {
            var response = await http.PostAsJsonAsync("/api/Device/Register", new DeviceRegistration
            {
                MacAddress = macAddress,
                Email = email,
                DevicePin = devicePin,
                ServiceType = 1,
                DisplayName = displayName,
            }, JsonOptions);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DeviceConfig>(JsonOptions);
        }

        public async Task<string> AuthenticateAsync(string apiId, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Device/Authenticate");
            request.Headers.Add("apiId", apiId);
            request.Headers.Add("apiKey", apiKey);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var auth = await response.Content.ReadFromJsonAsync<DeviceAuthentication>(JsonOptions);
            return auth?.apiAuth ?? throw new InvalidOperationException("Authenticate returned no apiAuth token.");
        }

        /// Always sends a ConfigVersion that can never match the server's - the simulator has no reason to cache/track it, this way every tick gets the current rules unconditionally.
        public async Task<DeviceConfig?> PollConfigAsync(string apiId, string apiAuth, string kit)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Device/Config")
            {
                Content = JsonContent.Create(new DeviceConfigPoll
                {
                    ConfigVersion = -1,
                    Uptime = Environment.TickCount64 / 1000,
                    Rssi = -50,
                    FreeHeap = 100_000,
                    FirmwareVersion = "Simulated",
                    Board = "Virtual",
                    Kit = kit,
                }, options: JsonOptions),
            };
            AddSessionHeaders(request, apiId, apiAuth);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return response.Content.Headers.ContentLength is 0 or null
                ? null // config already up to date per this poll - never actually happens with ConfigVersion=-1, but handled for correctness anyway
                : await response.Content.ReadFromJsonAsync<DeviceConfig>(JsonOptions);
        }

        public async Task PushSensorDataAsync(string apiId, string apiAuth, SimulatedReading reading)
        {
            var entry = new JsonObject
            {
                ["temperature"] = reading.Temperature,
                ["soilTemperature"] = reading.SoilTemperature,
                ["humidity"] = reading.Humidity,
                ["battery"] = reading.Battery,
                ["moisture"] = reading.Moisture,
                ["light"] = reading.Light,
                ["co2"] = reading.Co2,
                ["tvoc"] = reading.Tvoc,
                ["barometer"] = reading.Barometer,
                ["liquidPH"] = reading.LiquidPH,
                ["rainLevel"] = reading.RainLevel,
                ["waterLevel"] = reading.WaterLevel,
                ["wind"] = reading.Wind,
                ["dateCreated"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/SensorData") { Content = JsonContent.Create(new JsonArray(entry)) };
            AddSessionHeaders(request, apiId, apiAuth);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task PushControllerDataAsync(string apiId, string apiAuth, IList<ControllerDataPush> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ControllerData") { Content = JsonContent.Create(entries, options: JsonOptions) };
            AddSessionHeaders(request, apiId, apiAuth);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private static void AddSessionHeaders(HttpRequestMessage request, string apiId, string apiAuth)
        {
            request.Headers.Add("apiId", apiId);
            request.Headers.Authorization = new AuthenticationHeaderValue(apiAuth);
        }
    }
}
