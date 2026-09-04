using System.Text.Json;
using api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace api.Relay.LocalForwarding
{
    /// <summary>Profile A (WiFiRepeater): a local device points its ServicePoint at this relay's
    /// own address:port instead of api.agrumy.com and sends EXACTLY the same requests
    /// (apiId/apiKey headers, same JSON bodies) it would send AgrumyService directly - completely
    /// transparent to unmodified AgrumyFirmware, zero firmware changes. Each local call becomes one
    /// RelayBatchEntry forwarded through the SAME /api/Relay/Batch path Profile B uses, just always
    /// a batch of one and always flushed immediately (RelayMode only matters for Profile B - see
    /// api.Models.RelayMode's remarks for why a live blocked HTTP connection has no use for
    /// Aggregated batching).</summary>
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
                // AgrumyService unreachable - same "connection lost" shape a device's own direct
                // call would see, so its existing retry/buffer logic (ServiceController) applies
                // unchanged; the relay adds a hop, not a new failure mode to handle.
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
