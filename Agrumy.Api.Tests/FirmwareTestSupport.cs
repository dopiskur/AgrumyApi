using System.Text;
using api;
using api.Dal.Interface;
using api.Firmware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>Canned network for FirmwareCatalogService tests - URL -> text (GitHub API JSON, manifests) and URL -> bytes (.bin assets); an unknown URL throws like a real 404 would.</summary>
internal sealed class FakeFirmwareFetcher : IFirmwareFetcher
{
    public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Binaries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Requested { get; } = [];

    public Task<string> GetStringAsync(string url, bool gitHubApi, CancellationToken cancellationToken = default)
    {
        Requested.Add(url);
        return Texts.TryGetValue(url, out var text)
            ? Task.FromResult(text)
            : throw new HttpRequestException("404 " + url);
    }

    public Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
    {
        Requested.Add(url);
        return Binaries.TryGetValue(url, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new HttpRequestException("404 " + url);
    }

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}

internal static class FirmwareTestSupport
{
    /// <summary>A FirmwareStorage rooted in a fresh temp directory (absolute path, so the IHostEnvironment content root is never consulted).</summary>
    public static FirmwareStorage NewStorage(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "agrumy-fw-tests", Guid.NewGuid().ToString("N"));
        var settings = Options.Create(new AgrumySettings { FirmwareLocalPath = root });
        return new FirmwareStorage(settings, new Mock<IHostEnvironment>().Object);
    }

    /// <summary>The same IRepository mock backs every facet the service takes.</summary>
    public static FirmwareCatalogService NewCatalog(IRepository repo, IFirmwareFetcher? fetcher = null, FirmwareStorage? storage = null) =>
        new(repo, repo, repo, fetcher ?? new FakeFirmwareFetcher(), storage ?? NewStorage(out _), NullLogger<FirmwareCatalogService>.Instance);
}
