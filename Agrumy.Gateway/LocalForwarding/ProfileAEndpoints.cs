using System.Text.Json;
using api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace api.Gateway.LocalForwarding
{
    /// Profile A (WiFiRepeater): a local device points its ServicePoint at this gateway instead of api.agrumy.com, sending unmodified requests - each call becomes one always-immediate GatewayBatchEntry through the same /api/Gateway/Batch path Profile B uses.
    public static class ProfileAEndpoints
    {
        public static void MapProfileAEndpoints(this WebApplication app)
        {
            app.MapPost("/api/Device/Config", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, GatewayEntryType.Config));
            app.MapPost("/api/SensorData", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, GatewayEntryType.SensorData));
            app.MapPost("/api/Device/Event", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, GatewayEntryType.Event));
            app.MapPost("/api/Device/Command/Ack", (HttpRequest req, AgrumyServiceClient client) =>
                ForwardAsync(req, client, GatewayEntryType.CommandAck));
        }

        private static async Task<IResult> ForwardAsync(HttpRequest req, AgrumyServiceClient client, GatewayEntryType type)
        {
            string apiId = req.Headers["apiId"].ToString();
            string apiKey = req.Headers["apiKey"].ToString();
            if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(apiKey))
            {
                return Results.Unauthorized();
            }

            using JsonDocument body = await JsonDocument.ParseAsync(req.Body);
            var entry = new GatewayBatchEntry
            {
                DeviceApiId = apiId,
                DeviceApiKey = apiKey,
                Type = type,
                Payload = body.RootElement.Clone(),
            };

            GatewayBatchResponse response;
            try
            {
                response = await client.BatchAsync(new GatewayBatchRequest { Entries = [entry] }, req.HttpContext.RequestAborted);
            }
            catch (HttpRequestException)
            {
                // AgrumyService unreachable - same "connection lost" shape a device's own direct call would see, so its existing retry/buffer logic applies unchanged.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (GatewayRateLimitedException ex)
            {
                // A genuine 429, not a generic failure - the device's own apiConfig() recognizes this distinctly and waits the given window instead of counting it toward its reboot-on-repeated-failure escalation.
                req.HttpContext.Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            GatewayBatchEntryResult result = response.Results.Count > 0
                ? response.Results[0]
                : new GatewayBatchEntryResult { Success = false, StatusCode = 500, Error = "Empty batch response." };

            if (!result.Success)
            {
                return Results.Text(result.Error ?? "", statusCode: result.StatusCode);
            }
            // Mirrors GetConfig's shape: null Config means "up to date" (empty 200), populated means full DeviceConfig JSON.
            return result.Config != null ? Results.Ok(result.Config) : Results.Ok();
        }
    }
}
