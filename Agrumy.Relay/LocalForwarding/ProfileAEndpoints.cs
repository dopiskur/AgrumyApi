using System.Text.Json;
using api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace api.Relay.LocalForwarding
{
    /// Profile A (WiFiRepeater): a local device points its ServicePoint at this relay instead of api.agrumy.com, sending unmodified requests - each call becomes one always-immediate RelayBatchEntry through the same /api/Relay/Batch path Profile B uses.
    public static class ProfileAEndpoints
    {
        public static void MapProfileAEndpoints(this WebApplication app)
        {
            app.MapPost("/api/Device/Config", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, RelayEntryType.Config));
            app.MapPost("/api/SensorData", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, RelayEntryType.SensorData));
            app.MapPost("/api/Device/Event", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, RelayEntryType.Event));
            app.MapPost("/api/Device/Command/Ack", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, RelayEntryType.CommandAck));
        }

        private static async Task<IResult> ForwardAsync(HttpRequest req, AgrumyServiceClient client, RelayEntryType type)
        {
            string apiId = req.Headers["apiId"].ToString();
            string apiKey = req.Headers["apiKey"].ToString();
            if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(apiKey))
            {
                return Results.Unauthorized();
            }

            using JsonDocument body = await JsonDocument.ParseAsync(req.Body);
            var entry = new RelayBatchEntry
            {
                DeviceApiId = apiId,
                DeviceApiKey = apiKey,
                Type = type,
                Payload = body.RootElement.Clone(),
            };

            RelayBatchResponse response;
            try
            {
                response = await client.BatchAsync(new RelayBatchRequest { Entries = [entry] }, req.HttpContext.RequestAborted);
            }
            catch (HttpRequestException)
            {
                // AgrumyService unreachable - same "connection lost" shape a device's own direct call would see, so its existing retry/buffer logic applies unchanged.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            RelayBatchEntryResult result = response.Results.Count > 0
                ? response.Results[0]
                : new RelayBatchEntryResult { Success = false, StatusCode = 500, Error = "Empty batch response." };

            if (!result.Success)
            {
                return Results.Text(result.Error ?? "", statusCode: result.StatusCode);
            }
            // Config: mirrors GetConfig's own shape - a null Config body means "up to date, do
            // nothing" (empty 200), a populated one is the full DeviceConfig JSON.
            return result.Config != null ? Results.Ok(result.Config) : Results.Ok();
        }
    }
}
