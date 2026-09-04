using System.Net.Http.Headers;

namespace api.Firmware
{
    /// <summary>The only thing in the firmware code that touches the network, behind an interface so FirmwareCatalogService is unit-testable with canned GitHub/manifest responses.</summary>
    public interface IFirmwareFetcher
    {
        /// <summary>GET a JSON/text document. <paramref name="gitHubApi"/> adds the GitHub REST
        /// headers (Accept, User-Agent, optional token) - api.github.com rejects requests without a
        /// User-Agent outright.</summary>
        Task<string> GetStringAsync(string url, bool gitHubApi, CancellationToken cancellationToken = default);

        /// <summary>Streams a binary (a .bin asset) - follows redirects, since a GitHub release asset
        /// URL 302s to objects.githubusercontent.com.</summary>
        Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default);
    }

    public sealed class HttpFirmwareFetcher(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<AgrumySettings> settings) : IFirmwareFetcher
    {
        public const string ClientName = "firmware";

        // AllowAutoRedirect=false (Program.cs) so every hop below goes back through SsrfGuard, not just the first URL.
        private const int MaxRedirects = 5;

        public async Task<string> GetStringAsync(string url, bool gitHubApi, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage response = await SendAsync(url, gitHubApi, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
        {
            // Not wrapped in `using` on purpose - the returned Stream is backed by this response and
            // must outlive it; the caller owns disposing the stream (same contract as before this change).
            HttpResponseMessage response = await SendAsync(url, gitHubApi: false, cancellationToken);
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        private async Task<HttpResponseMessage> SendAsync(string url, bool gitHubApi, CancellationToken cancellationToken)
        {
            HttpClient client = httpClientFactory.CreateClient(ClientName);
            Uri current = new(url);

            for (int hop = 0; ; hop++)
            {
                await SsrfGuard.EnsureAllowedAsync(current, cancellationToken);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                if (gitHubApi)
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                    // Optional: unauthenticated GitHub allows 60 requests/hour per IP, plenty for an admin clicking "refresh" - a token only matters for a busy shared egress IP.
                    if (!string.IsNullOrWhiteSpace(settings.Value.FirmwareGitHubToken))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.FirmwareGitHubToken);
                    }
                }

                HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsRedirect(response.StatusCode))
                {
                    Uri? location = response.Headers.Location;
                    response.Dispose();
                    if (location == null)
                    {
                        throw new HttpRequestException($"Redirect ({(int)response.StatusCode}) with no Location header.");
                    }
                    if (hop >= MaxRedirects)
                    {
                        throw new HttpRequestException($"Too many redirects fetching '{url}'.");
                    }
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return response;
            }
        }

        private static bool IsRedirect(System.Net.HttpStatusCode status) =>
            status is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found
                or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect
                or System.Net.HttpStatusCode.PermanentRedirect;
    }
}
