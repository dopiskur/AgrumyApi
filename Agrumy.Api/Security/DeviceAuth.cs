using System.Security.Cryptography;
using System.Text;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace api.Security
{
    /// <summary>
    /// Authorization for the device-communication endpoints, which do not use a user JWT.
    ///
    /// - <see cref="ApiKeyPolicy"/> ("DeviceApiKey"): the device presents its permanent
    ///   <c>apiId</c> + <c>apiKey</c> headers; used by <c>POST /api/Device/Authenticate</c> to
    ///   bootstrap a session.
    /// - <see cref="SessionPolicy"/> ("DeviceSession"): the device presents <c>apiId</c> plus the
    ///   short-lived <c>apiAuth</c> token (issued by Authenticate, cached server-side) in the
    ///   <c>Authorization</c> header; used by Config and SensorData POST.
    ///
    /// On success the handler stashes the verified apiId in <c>HttpContext.Items["apiId"]</c>;
    /// read it with <see cref="HttpContextExtensions.DeviceApiId"/>.
    /// </summary>
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
        /// <summary>The apiId verified by the DeviceApiKey / DeviceSession authorization handler, or null.</summary>
        public static string? DeviceApiId(this HttpContext http) =>
            http.Items.TryGetValue(DeviceAuth.ApiIdItemKey, out var v) ? v as string : null;
    }

    public sealed class DeviceApiKeyRequirement : IAuthorizationRequirement;

    // Narrow IDeviceRepository facet (roadmap #74) - the only data-layer call here is the ApiId lookup.
    // Roadmap #105: ILogger added so a rejection reaches the log instead of a bare `return` - the
    // client-facing result is an identical 401 either way (no info leak), but server-side a
    // firmware bug (missing header), a misconfig/attack (bad key) and an unknown apiId used to be
    // indistinguishable.
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

            // apiId is an identifier, not a secret (see DeviceApiController.DeviceRegistration) -
            // safe to log in full; apiKey is never logged (roadmap #20 masking standard - here,
            // simply never touches the log at all).
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

        // Roadmap #105: a cache miss (never authenticated / evicted) and a TTL-expired entry
        // (roadmap #109) both surface as GetDeviceCacheAsync returning an empty DeviceCache -
        // indistinguishable from here, so both fall under this one category rather than guessing.
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
