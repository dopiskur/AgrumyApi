using System.Net.Http.Json;
using api.Models;
using api.Relay.Registration;
using Microsoft.Extensions.Options;

namespace api.Relay
{
    /// <summary>Thin HTTP client for the two calls a relay makes to AgrumyService - registration
    /// (once) and Batch (repeatedly). Plain typed HttpClient rather than Refit (Agrumy.Web's own
    /// convention): two endpoints do not earn Refit's interface/source-generator ceremony the way
    /// Agrumy.Web's much larger surface does.</summary>
    public class AgrumyServiceClient(HttpClient http, RelayRegistrationStore registration, IOptions<RelayOptions> options)
    {
        private readonly RelaySelfOptions self = options.Value.Relay;

        public async Task<RelayRegistrationState> RegisterAsync(CancellationToken ct)
        {
            var request = new DeviceRegistration
            {
                Email = options.Value.AgrumyService.Email,
                DevicePin = options.Value.AgrumyService.DevicePin,
                MacAddress = self.MacAddress,
                IsRelay = true,
                RelayProfile = self.Profile,
            };

            HttpResponseMessage response = await http.PostAsJsonAsync("/api/Device/Register", request, ct);
            response.EnsureSuccessStatusCode();
            DeviceConfig config = (await response.Content.ReadFromJsonAsync<DeviceConfig>(ct))
                ?? throw new InvalidOperationException("Register returned an empty body.");

            return new RelayRegistrationState
            {
                ApiId = config.ApiId,
                ApiKey = config.ApiKey,
                IdDevice = config.deviceID,
            };
        }

        /// <summary>Authenticates with THIS relay's own apiId/apiKey (DeviceAuth.ApiKeyPolicy on
        /// the server side) - a relay re-sends its permanent credential on every Batch call rather
        /// than bootstrapping a short-lived apiAuth session first, since Batch calls are already
        /// infrequent/heavy relative to one device's own chatty per-poll session flow.</summary>
        public async Task<RelayBatchResponse> BatchAsync(RelayBatchRequest request, CancellationToken ct)
        {
            RelayRegistrationState reg = registration.Current;
            if (!reg.IsComplete)
            {
                throw new InvalidOperationException("Relay is not registered yet.");
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/Relay/Batch")
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Add("apiId", reg.ApiId);
            message.Headers.Add("apiKey", reg.ApiKey);

            HttpResponseMessage response = await http.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<RelayBatchResponse>(ct))
                ?? new RelayBatchResponse();
        }

        /// <summary>This relay's own DevEUI-&gt;device mapping (Profile B) - GET
        /// /api/Relay/DeviceMapping, same apiId/apiKey auth as Batch.</summary>
        public async Task<IList<RelayDeviceMapping>> GetDeviceMappingAsync(CancellationToken ct)
        {
            RelayRegistrationState reg = registration.Current;
            if (!reg.IsComplete)
            {
                return [];
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, "/api/Relay/DeviceMapping");
            message.Headers.Add("apiId", reg.ApiId);
            message.Headers.Add("apiKey", reg.ApiKey);

            HttpResponseMessage response = await http.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<IList<RelayDeviceMapping>>(ct)) ?? [];
        }
    }
}
