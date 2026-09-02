using System.Net.Http.Headers;

namespace api.Firmware
{
    /// <summary>Roadmap #94: the only thing in the firmware code that touches the network, behind
    /// an interface so FirmwareCatalogService is unit-testable with canned GitHub/manifest
    /// responses (same reasoning as INotificationChannel in roadmap #6).</summary>
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

        public async Task<string> GetStringAsync(string url, bool gitHubApi, CancellationToken cancellationToken = default)
        {
            HttpClient client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (gitHubApi)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                // Optional: unauthenticated GitHub allows 60 requests/hour per IP, which is plenty
                // for an admin clicking "refresh" - a token only matters for a busy shared egress IP.
                if (!string.IsNullOrWhiteSpace(settings.Value.FirmwareGitHubToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.FirmwareGitHubToken);
                }
            }
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        public async Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
        {
            HttpClient client = httpClientFactory.CreateClient(ClientName);
            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
    }
}
