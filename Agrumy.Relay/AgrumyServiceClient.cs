using System.Net.Http.Json;
using api.Models;
using api.Relay.Registration;
using Microsoft.Extensions.Options;

namespace api.Relay
{
    /// Thin HTTP client for the two calls a relay makes to AgrumyService (registration once, Batch repeatedly) - plain typed HttpClient, not Refit, since two endpoints don't earn that ceremony.
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
                RelayRegistrationSecret = self.RegistrationSecret,
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

        /// Authenticates with THIS relay's own apiId/apiKey (DeviceAuth.ApiKeyPolicy) - re-sends the permanent credential on every Batch call rather than bootstrapping a short-lived apiAuth session first.
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

        /// This relay's own DevEUI->device mapping (Profile B) - GET /api/Relay/DeviceMapping, same apiId/apiKey auth as Batch.
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
