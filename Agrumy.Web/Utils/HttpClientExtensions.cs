using System.Net.Http.Json;
using System.Text.Json;

namespace api.Utils
{
    /// <summary>Raised when Agrumy.Api answers a call from the web app with a non-success status.</summary>
    public sealed class ApiException(int statusCode, string body)
        : Exception(string.IsNullOrWhiteSpace(body) ? $"API call failed ({statusCode})." : body)
    {
        public int StatusCode { get; } = statusCode;

        /// <summary>The raw response body (often the API's <c>{ reason, message }</c> shape or a plain string).</summary>
        public string Body { get; } = body ?? "";
    }

    public static class HttpClientExtensions
    {
        /// <summary>
        /// Web defaults: camelCase, case-insensitive property matching, numbers readable from
        /// strings - matches how Agrumy.Api's System.Text.Json serialises responses.
        /// </summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Reads a JSON body of type <typeparamref name="T"/>. On a non-success status it throws
        /// <see cref="ApiException"/> carrying the response body, so the caller can surface the
        /// API's actual error message instead of a bare status code.
        /// </summary>
        public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException((int)response.StatusCode, await SafeBody(response));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json).ConfigureAwait(false);
            return value ?? throw new ApiException((int)response.StatusCode,
                $"API returned an empty or invalid body for {typeof(T).Name}.");
        }

        /// <summary>Reads the raw response body as a string, throwing <see cref="ApiException"/> on a non-success status.</summary>
        public static async Task<string> ReadStringAsync(this HttpResponseMessage response)
        {
            string body = await SafeBody(response);
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException((int)response.StatusCode, body);
            }
            return body;
        }

        private static async Task<string> SafeBody(HttpResponseMessage response)
        {
            try { return await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch { return response.ReasonPhrase ?? ""; }
        }
    }
}
