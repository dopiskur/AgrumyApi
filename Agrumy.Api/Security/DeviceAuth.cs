using System.Security.Cryptography;
using System.Text;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;

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

    public sealed class DeviceApiKeyHandler(IRepository repo) : AuthorizationHandler<DeviceApiKeyRequirement>
    {
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
                return;
            }

            Device? device = await repo.DeviceGetByApiIdAsync(apiId);
            if (device is not null && DeviceAuth.ConstantTimeEquals(apiKey, device.ApiKey))
            {
                http.Items[DeviceAuth.ApiIdItemKey] = apiId;
                context.Succeed(requirement);
            }
        }
    }

    public sealed class DeviceSessionRequirement : IAuthorizationRequirement;

    public sealed class DeviceSessionHandler(ICache cache) : AuthorizationHandler<DeviceSessionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, DeviceSessionRequirement requirement)
        {
            if (context.Resource is not HttpContext http)
            {
                return Task.CompletedTask;
            }

            string apiId = http.Request.Headers["apiId"].ToString();
            string token = DeviceAuth.ReadAuthToken(http);
            if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(token))
            {
                return Task.CompletedTask;
            }

            var deviceCache = cache.GetDeviceCache(apiId);
            if (DeviceAuth.ConstantTimeEquals(token, deviceCache?.apiAuth))
            {
                http.Items[DeviceAuth.ApiIdItemKey] = apiId;
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
