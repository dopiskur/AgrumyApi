using System.Security.Cryptography;
using System.Text;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace api.Security
{
    /// Authorization for device-communication endpoints (no user JWT): <see cref="ApiKeyPolicy"/> needs the permanent apiId+apiKey headers (Authenticate bootstrap), <see cref="SessionPolicy"/> needs apiId plus the short-lived apiAuth token (Config/SensorData); either lands the verified apiId in <c>HttpContext.Items</c>, read via <see cref="HttpContextExtensions.DeviceApiId"/>.
    public static class DeviceAuth
    {
        public const string ApiKeyPolicy = "DeviceApiKey";
        public const string SessionPolicy = "DeviceSession";
        public const string ApiIdItemKey = "apiId";

        internal static bool ConstantTimeEquals(string? a, string? b)
        {
            if (a is null || b is null)
            {
                return false;
            }
            byte[] x = Encoding.UTF8.GetBytes(a);
            byte[] y = Encoding.UTF8.GetBytes(b);
            return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
        }

        internal static string ReadAuthToken(HttpContext http)
        {
            string raw = http.Request.Headers.Authorization.ToString();
            return raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? raw["Bearer ".Length..] : raw;
        }
    }

    public static class HttpContextExtensions
    {
        /// The apiId verified by the DeviceApiKey / DeviceSession authorization handler, or null.
        public static string? DeviceApiId(this HttpContext http) =>
            http.Items.TryGetValue(DeviceAuth.ApiIdItemKey, out var v) ? v as string : null;
    }

    public sealed class DeviceApiKeyRequirement : IAuthorizationRequirement;

    // ILogger lets a rejection reach the log instead of a bare `return` - the client-facing result is an identical 401 either way (no info leak), but server-side a firmware bug, a misconfig/attack, and an unknown apiId are now distinguishable.
    public sealed partial class DeviceApiKeyHandler(IDeviceRepository repo, ILogger<DeviceApiKeyHandler> logger)
        : AuthorizationHandler<DeviceApiKeyRequirement>
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Device ApiKey auth rejected: missing apiId/apiKey header.")]
        private static partial void LogMissingHeader(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Device ApiKey auth rejected: unknown apiId {ApiId}.")]
        private static partial void LogUnknownDevice(ILogger logger, string apiId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Device ApiKey auth rejected: apiKey mismatch for apiId {ApiId}.")]
        private static partial void LogBadKey(ILogger logger, string apiId);

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, DeviceApiKeyRequirement requirement)
        {
            if (context.Resource is not HttpContext http)
            {
                return;
            }

            string apiId = http.Request.Headers["apiId"].ToString();
            string apiKey = http.Request.Headers["apiKey"].ToString();
            if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(apiKey))
            {
                LogMissingHeader(logger);
                return;
            }

            Device? device = await repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                LogUnknownDevice(logger, apiId);
                return;
            }

            // apiId is an identifier, not a secret - safe to log in full; apiKey is never logged.
            if (DeviceAuth.ConstantTimeEquals(apiKey, device.ApiKey))
            {
                http.Items[DeviceAuth.ApiIdItemKey] = apiId;
                context.Succeed(requirement);
            }
            else
            {
                LogBadKey(logger, apiId);
            }
        }
    }

    public sealed class DeviceSessionRequirement : IAuthorizationRequirement;

    public sealed partial class DeviceSessionHandler(ICache cache, ILogger<DeviceSessionHandler> logger)
        : AuthorizationHandler<DeviceSessionRequirement>
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Device Session auth rejected: missing apiId header or Authorization token.")]
        private static partial void LogMissingHeader(ILogger logger);

        // A cache miss (never authenticated / evicted) and a TTL-expired entry both surface as GetDeviceCacheAsync returning an empty DeviceCache - indistinguishable from here, so both fall under this one category.
        [LoggerMessage(Level = LogLevel.Warning, Message = "Device Session auth rejected: no valid session for apiId {ApiId}.")]
        private static partial void LogExpiredSession(ILogger logger, string apiId);

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, DeviceSessionRequirement requirement)
        {
            if (context.Resource is not HttpContext http)
            {
                return;
            }

            string apiId = http.Request.Headers["apiId"].ToString();
            string token = DeviceAuth.ReadAuthToken(http);
            if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(token))
            {
                LogMissingHeader(logger);
                return;
            }

            var deviceCache = await cache.GetDeviceCacheAsync(apiId);
            if (DeviceAuth.ConstantTimeEquals(token, deviceCache.apiAuth))
            {
                http.Items[DeviceAuth.ApiIdItemKey] = apiId;
                context.Succeed(requirement);
            }
            else
            {
                LogExpiredSession(logger, apiId);
            }
        }
    }
}
