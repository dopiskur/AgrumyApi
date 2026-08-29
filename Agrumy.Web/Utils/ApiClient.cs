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

    /// <summary>Refit configuration for the <see cref="api.Dal.Interface.IApi"/> client (roadmap #32).</summary>
    public static class RefitConfig
    {
        public static readonly RefitSettings Settings = new()
        {
            // Replace Refit's default exception (message is just the status line) with one that
            // carries the response body, so failures like "email already registered" reach the UI.
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
