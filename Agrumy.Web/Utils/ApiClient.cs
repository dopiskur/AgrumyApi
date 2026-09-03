using System.Text.Json;
using Refit;

namespace api.Utils
{
    /// <summary>Raised when Agrumy.Api answers a call from the web app with a non-success status.</summary>
    public sealed class ApiException(int statusCode, string body)
        : Exception(string.IsNullOrWhiteSpace(body) ? $"API call failed ({statusCode})." : body)
    {
        public int StatusCode { get; } = statusCode;

        /// <summary>The raw response body - often the API's <c>{ reason, message }</c> shape or a plain string.</summary>
        public string Body { get; } = body ?? "";
    }

    /// <summary>Refit configuration for the <see cref="api.Dal.Interface.IApi"/> client.</summary>
    public static class RefitConfig
    {
        public static readonly RefitSettings Settings = new()
        {
            // Refit's default ContentSerializer adds a global JsonStringEnumConverter, which breaks DTOs that deliberately stay numeric - use plain JsonSerializerDefaults.Web instead.
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)),

            // Carry the response body (Refit's default exception only has the status line) so failures like "email already registered" reach the UI.
            ExceptionFactory = async response =>
            {
                if (response.IsSuccessStatusCode)
                {
                    return null;
                }

                string body;
                try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { body = response.ReasonPhrase ?? ""; }

                return new ApiException((int)response.StatusCode, body);
            },
        };
    }
}
