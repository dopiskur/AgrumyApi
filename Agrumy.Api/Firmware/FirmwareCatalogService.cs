using System.Text.Json;
using System.Text.Json.Serialization;
using api.Dal.Interface;
using api.Models;

namespace api.Firmware
{
    /// <summary>Roadmap #94/#93 business logic over the deviceFirmware catalog: which source is
    /// active and which rows a device may be offered, the three ways a Local repository gets
    /// populated (pull from GitHub, import a server-side directory, manual upload), the GitHub /
    /// Custom-manifest reads, and the per-device offer resolution BuildDeviceConfigAsync uses. Kept
    /// separate from FirmwareApiController/DeviceApiController the same way CommandQueueService is
    /// (roadmap #34) - directly unit-testable with a mocked repository and a canned IFirmwareFetcher.</summary>
    public sealed class FirmwareCatalogService(
        IFirmwareRepository firmwareRepo,
        IServerConfigRepository configRepo,
        IDeviceRepository deviceRepo,
        IFirmwareFetcher fetcher,
        FirmwareStorage storage,
        ILogger<FirmwareCatalogService> log)
    {
        public const string ManifestFileName = "manifest.json";

        // Manifest/GitHub JSON: camelCase on our side, snake_case attributes on GitHub's records.
        private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary>Which catalog rows a device may be offered: the ACTIVE source's rows, plus
        /// Local ones always - a manually uploaded .bin (#93-c-2) is hosted by this API regardless
        /// of what the default source is, so hiding it just because GitHub is selected would make
        /// "upload and install this build" silently do nothing.</summary>
        public static IReadOnlyCollection<FirmwareSource> VisibleSources(FirmwareSource active) =>
            active == FirmwareSource.Local ? [FirmwareSource.Local] : [active, FirmwareSource.Local];

        public static string LocalDownloadUrl(string publicBaseUrl, string fileName) =>
            publicBaseUrl.TrimEnd('/') + "/api/Firmware/Download/" + fileName;

        // ---- reads -------------------------------------------------------------------------

        /// <summary>Whole catalog (every source, legacy rows included), board then newest version first.</summary>
        public async Task<IList<DeviceFirmware>> ListAsync()
        {
            IList<DeviceFirmware> rows = await firmwareRepo.FirmwareListAsync();
            return rows
                .OrderBy(r => r.Board ?? "~")
                .ThenByDescending(r => FirmwareVersion.TryParse(r.Version, out var v) ? v : default)
                .ToList();
        }

        /// <summary>Catalog entries a device on <paramref name="board"/> may be offered, newest first.</summary>
        public async Task<IList<DeviceFirmware>> ListForBoardAsync(string board)
        {
            FirmwareSource active = (await configRepo.ServerConfigGetAsync(1)).FirmwareSource;
            IList<DeviceFirmware> rows = await firmwareRepo.FirmwareListForBoardAsync(board, VisibleSources(active));
            return rows
                .Where(r => FirmwareVersion.IsValid(r.Version))
                .OrderByDescending(r => FirmwareVersion.Parse(r.Version!))
                .ToList();
        }

        public async Task<DeviceFirmware?> LatestForBoardAsync(string board) =>
            (await ListForBoardAsync(board)).FirstOrDefault();

        /// <summary>The build to offer <paramref name="device"/> on this config poll, or null when
        /// nothing should be offered: FirmwareUpdate must be set; a pinned FirmwareTargetVersion wins
        /// over "latest"; a device that has never reported its Board (pre-#94 firmware) falls back to
        /// the legacy per-DeviceTypeID row so an old fleet still updates to a firmware that WILL
        /// report it.</summary>
        public async Task<DeviceFirmware?> ResolveOfferAsync(Device device, string? board)
        {
            if (device.FirmwareUpdate != true)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(board))
            {
                IList<DeviceFirmware> candidates = await ListForBoardAsync(board);
                DeviceFirmware? match = device.FirmwareTargetVersion is { Length: > 0 } target
                    ? candidates.FirstOrDefault(c => FirmwareVersion.AreEqual(c.Version, target))
                    : candidates.FirstOrDefault();
                if (match != null)
                {
                    return match;
                }
            }

            return device.DeviceTypeID == null ? null : await deviceRepo.DeviceFirmwareLatestGetAsync(device.DeviceTypeID);
        }

        // ---- per-device update request (roadmap #93) ---------------------------------------

        /// <summary>Arms the OTA flag. Returns an error message (for a 400) when a specific version
        /// was requested that the catalog does not have for the device's board, or when the device
        /// has never reported a board and a specific version is therefore unresolvable.</summary>
        public async Task<string?> RequestUpdateAsync(Device device, string? version)
        {
            int idDevice = device.IDDevice!.Value;
            if (string.IsNullOrWhiteSpace(version))
            {
                await firmwareRepo.DeviceFirmwareUpdateSetAsync(idDevice, true, null);
                return null;
            }

            string? normalized = FirmwareVersion.Normalize(version);
            if (normalized == null)
            {
                return $"'{version}' is not a valid version.";
            }
            string? board = await firmwareRepo.DeviceBoardGetAsync(idDevice);
            if (board == null)
            {
                return "The device has not reported its board yet - only \"latest\" can be requested until it polls with a firmware that does.";
            }
            IList<DeviceFirmware> candidates = await ListForBoardAsync(board);
            if (!candidates.Any(c => FirmwareVersion.AreEqual(c.Version, normalized)))
            {
                return $"Version {normalized} is not in the catalog for board {board}.";
            }
            await firmwareRepo.DeviceFirmwareUpdateSetAsync(idDevice, true, normalized);
            return null;
        }

        public Task CancelUpdateAsync(int idDevice) => firmwareRepo.DeviceFirmwareUpdateSetAsync(idDevice, false, null);

        /// <summary>Called from GetConfig with the heartbeat: once the device reports the version it
        /// was asked to run, the request is fulfilled - clear it so the UI stops showing "pending"
        /// and the next poll stops carrying an OTA offer. Returns true on that transition.</summary>
        public async Task<bool> NoteHeartbeatAsync(Device device, string? runningVersion, string? board)
        {
            if (device.FirmwareUpdate != true || string.IsNullOrWhiteSpace(runningVersion))
            {
                return false;
            }
            string? expected = device.FirmwareTargetVersion;
            if (expected == null)
            {
                expected = (await ResolveOfferAsync(device, board))?.Version;
            }
            if (expected == null || !FirmwareVersion.AreEqual(expected, runningVersion))
            {
                return false;
            }
            await firmwareRepo.DeviceFirmwareUpdateSetAsync(device.IDDevice!.Value, false, null);
            return true;
        }

        // ---- sync from the active/remote source ------------------------------------------

        public async Task<FirmwareSyncResult> SyncAsync(FirmwareSyncMode mode, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            ServerConfig config = await configRepo.ServerConfigGetAsync(1);
            string repository = config.FirmwareGitHubRepository ?? "dopiskur/AgrumyDevice";

            if (mode == FirmwareSyncMode.Refresh)
            {
                switch (config.FirmwareSource)
                {
                    case FirmwareSource.GitHub:
                        return await ReplaceSourceRowsAsync(FirmwareSource.GitHub, await FetchGitHubReleasesAsync(repository, cancellationToken));
                    case FirmwareSource.Custom:
                        if (string.IsNullOrWhiteSpace(config.FirmwareCustomRepositoryUrl))
                        {
                            return new FirmwareSyncResult { Warnings = { "Custom repository manifest URL is not set (Server Settings)." } };
                        }
                        return await ReplaceSourceRowsAsync(FirmwareSource.Custom, await FetchCustomManifestAsync(config.FirmwareCustomRepositoryUrl, cancellationToken));
                    default:
                        // Local has no remote to "refresh" from - the closest meaningful action.
                        mode = FirmwareSyncMode.PullIncremental;
                        break;
                }
            }

            // PullFull / PullIncremental (roadmap #94-2a): GitHub -> local store, whichever source is
            // currently active - an admin prepares the Local repository BEFORE switching to it.
            var result = new FirmwareSyncResult();
            if (mode == FirmwareSyncMode.PullFull)
            {
                foreach (var row in (await firmwareRepo.FirmwareListAsync()).Where(r => r.Source == FirmwareSource.Local && r.FileName != null))
                {
                    storage.Delete(row.FileName!);
                }
                result.Removed = await firmwareRepo.FirmwareDeleteBySourceAsync(FirmwareSource.Local);
            }

            IList<DeviceFirmware> existingLocal = (await firmwareRepo.FirmwareListAsync()).Where(r => r.Source == FirmwareSource.Local).ToList();
            foreach (RemoteFile remote in await FetchGitHubReleasesAsync(repository, cancellationToken))
            {
                if (existingLocal.Any(e => e.Board == remote.Board && FirmwareVersion.AreEqual(e.Version, remote.Version)))
                {
                    result.Skipped++;
                    continue;
                }
                try
                {
                    await using Stream content = await fetcher.GetStreamAsync(remote.Url, cancellationToken);
                    (long size, string sha) = await storage.SaveAsync(remote.FileName, content, cancellationToken);
                    if (remote.Sha256 != null && !string.Equals(remote.Sha256, sha, StringComparison.OrdinalIgnoreCase))
                    {
                        storage.Delete(remote.FileName);
                        result.Warnings.Add($"{remote.FileName}: SHA-256 mismatch against the release manifest - discarded.");
                        continue;
                    }
                    await firmwareRepo.FirmwareAddAsync(new DeviceFirmware
                    {
                        Board = remote.Board,
                        Version = remote.Version,
                        FileName = remote.FileName,
                        Url = LocalDownloadUrl(publicBaseUrl, remote.FileName),
                        Source = FirmwareSource.Local,
                        SizeBytes = size,
                        Sha256 = sha,
                        PublishedAt = remote.PublishedAt,
                    });
                    result.Added++;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    log.LogWarning(ex, "Firmware pull of {File} failed", remote.FileName);
                    result.Warnings.Add($"{remote.FileName}: download failed ({ex.Message}).");
                }
            }
            return result;
        }

        private async Task<FirmwareSyncResult> ReplaceSourceRowsAsync(FirmwareSource source, IReadOnlyList<RemoteFile> remoteFiles)
        {
            var result = new FirmwareSyncResult { Removed = await firmwareRepo.FirmwareDeleteBySourceAsync(source) };
            foreach (RemoteFile remote in remoteFiles)
            {
                await firmwareRepo.FirmwareAddAsync(new DeviceFirmware
                {
                    Board = remote.Board,
                    Version = remote.Version,
                    FileName = remote.FileName,
                    Url = remote.Url,
                    Source = source,
                    SizeBytes = remote.SizeBytes,
                    Sha256 = remote.Sha256,
                    PublishedAt = remote.PublishedAt,
                });
                result.Added++;
            }
            return result;
        }

        // ---- roadmap #94-2b: import a directory on this server (mounted USB) -----------------

        public async Task<FirmwareSyncResult> ImportFromDirectoryAsync(string? path, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            var result = new FirmwareSyncResult();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                result.Warnings.Add($"Directory not found on the server: '{path}'.");
                return result;
            }

            Dictionary<string, string> manifestSha = new(StringComparer.OrdinalIgnoreCase);
            string manifestPath = Path.Combine(path, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                try
                {
                    FirmwareManifest? manifest = JsonSerializer.Deserialize<FirmwareManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), ManifestJson);
                    foreach (var file in (manifest?.Releases ?? []).SelectMany(r => r.Files))
                    {
                        if (file.FileName != null && file.Sha256 != null)
                        {
                            manifestSha[file.FileName] = file.Sha256;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    result.Warnings.Add($"{ManifestFileName} is not valid - files imported without checksum verification ({ex.Message}).");
                }
            }
            else
            {
                result.Warnings.Add($"No {ManifestFileName} in the directory - files imported without checksum verification.");
            }

            foreach (string filePath in Directory.EnumerateFiles(path, "*.bin"))
            {
                string fileName = Path.GetFileName(filePath);
                if (!FirmwareVersion.TryParseFileName(fileName, out string board, out string version))
                {
                    result.Warnings.Add($"{fileName}: not in the agrumy-<board>-v<version>.bin naming convention - skipped.");
                    result.Skipped++;
                    continue;
                }
                if (manifestSha.TryGetValue(fileName, out string? expectedSha))
                {
                    string actualSha = await FirmwareStorage.ComputeSha256Async(filePath, cancellationToken);
                    if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"{fileName}: SHA-256 does not match {ManifestFileName} - skipped (corrupted transfer?).");
                        result.Skipped++;
                        continue;
                    }
                }

                await using FileStream content = File.OpenRead(filePath);
                await AddLocalAsync(board, version, fileName, content, publicBaseUrl, cancellationToken);
                result.Added++;
            }
            return result;
        }

        // ---- roadmap #94-2c / #93-c-2: manual upload ---------------------------------------

        /// <summary>Returns the error (for a 400) when the file name is not in the convention.</summary>
        public async Task<(DeviceFirmware? Firmware, string? Error)> UploadAsync(string? fileName, Stream content, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            if (!FirmwareVersion.TryParseFileName(fileName, out string board, out string version))
            {
                return (null, $"'{fileName}' must be named agrumy-<board>-v<version>.bin (e.g. agrumy-esp32dev-v1.2.0.bin).");
            }
            return (await AddLocalAsync(board, version, fileName!, content, publicBaseUrl, cancellationToken), null);
        }

        private async Task<DeviceFirmware> AddLocalAsync(string board, string version, string fileName, Stream content, string publicBaseUrl, CancellationToken cancellationToken)
        {
            (long size, string sha) = await storage.SaveAsync(fileName, content, cancellationToken);

            // Same board+version already stored locally: the new file replaced it on disk, so the
            // stale row (old size/sha) must go too - one row per (board, version, source).
            foreach (var stale in (await firmwareRepo.FirmwareListForBoardAsync(board, [FirmwareSource.Local]))
                         .Where(r => FirmwareVersion.AreEqual(r.Version, version) && r.IDDeviceFirmware != null))
            {
                await firmwareRepo.FirmwareDeleteAsync(stale.IDDeviceFirmware!.Value);
            }

            var firmware = new DeviceFirmware
            {
                Board = board,
                Version = FirmwareVersion.Normalize(version),
                FileName = fileName,
                Url = LocalDownloadUrl(publicBaseUrl, fileName),
                Source = FirmwareSource.Local,
                SizeBytes = size,
                Sha256 = sha,
                PublishedAt = DateTime.UtcNow,
            };
            firmware.IDDeviceFirmware = await firmwareRepo.FirmwareAddAsync(firmware);
            return firmware;
        }

        public async Task<bool> DeleteAsync(int idDeviceFirmware)
        {
            DeviceFirmware? row = await firmwareRepo.FirmwareGetAsync(idDeviceFirmware);
            if (row == null)
            {
                return false;
            }
            if (row.Source == FirmwareSource.Local && row.FileName != null)
            {
                storage.Delete(row.FileName);
            }
            await firmwareRepo.FirmwareDeleteAsync(idDeviceFirmware);
            return true;
        }

        // ---- manifest + file access (roadmap #94-C1 browser tool, Custom repositories) --------

        /// <summary>The visible catalog in manifest.json form - what the browser "Build offline
        /// repo" tool copies onto a USB stick (URLs stripped there) and what another Agrumy install
        /// pointed at this one as a Custom repository reads.</summary>
        public async Task<FirmwareManifest> BuildManifestAsync(string publicBaseUrl)
        {
            FirmwareSource active = (await configRepo.ServerConfigGetAsync(1)).FirmwareSource;
            var visible = VisibleSources(active);
            IList<DeviceFirmware> rows = await firmwareRepo.FirmwareListAsync();
            var manifest = new FirmwareManifest
            {
                GeneratedAt = DateTime.UtcNow,
                Source = $"agrumy:{publicBaseUrl}",
            };
            foreach (var group in rows
                         .Where(r => r.Board != null && r.FileName != null && visible.Contains(r.Source) && FirmwareVersion.IsValid(r.Version))
                         .GroupBy(r => FirmwareVersion.Normalize(r.Version)!)
                         .OrderByDescending(g => FirmwareVersion.Parse(g.Key)))
            {
                manifest.Releases.Add(new FirmwareManifestRelease
                {
                    Version = group.Key,
                    PublishedAt = group.Max(r => r.PublishedAt),
                    Files = group
                        .GroupBy(r => r.Board!) // a board present from two sources: one entry, Local preferred (served by this host)
                        .Select(b => b.OrderBy(r => r.Source == FirmwareSource.Local ? 0 : 1).First())
                        .Select(r => new FirmwareManifestFile
                        {
                            Board = r.Board,
                            FileName = r.FileName,
                            SizeBytes = r.SizeBytes,
                            Sha256 = r.Sha256,
                            Url = r.Url,
                        })
                        .ToList(),
                });
            }
            return manifest;
        }

        /// <summary>Opens a catalog entry's bytes wherever they live - the local store, or streamed
        /// through from the remote URL so the browser tool never has to fetch GitHub cross-origin.
        /// Keyed by file name (what the manifest carries), Local preferred when two sources have it.</summary>
        public async Task<(Stream Content, string FileName)?> OpenAsync(string? fileName, CancellationToken cancellationToken = default)
        {
            if (!FirmwareVersion.TryParseFileName(fileName, out string board, out _))
            {
                return null;
            }
            FirmwareSource active = (await configRepo.ServerConfigGetAsync(1)).FirmwareSource;
            DeviceFirmware? row = (await firmwareRepo.FirmwareListForBoardAsync(board, VisibleSources(active)))
                .Where(r => string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Source == FirmwareSource.Local ? 0 : 1)
                .FirstOrDefault();
            if (row?.FileName == null)
            {
                return null;
            }
            if (row.Source == FirmwareSource.Local)
            {
                return storage.Exists(row.FileName) ? (File.OpenRead(storage.PathFor(row.FileName)), row.FileName) : null;
            }
            if (string.IsNullOrWhiteSpace(row.Url))
            {
                return null;
            }
            return (await fetcher.GetStreamAsync(row.Url, cancellationToken), row.FileName);
        }

        // ---- remote readers ------------------------------------------------------------------

        internal sealed record RemoteFile(string Board, string Version, string FileName, string Url, long? SizeBytes, string? Sha256, DateTime? PublishedAt);

        private sealed record GitHubRelease(
            [property: JsonPropertyName("tag_name")] string? TagName,
            [property: JsonPropertyName("draft")] bool Draft,
            [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
            [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

        private sealed record GitHubAsset(
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
            [property: JsonPropertyName("size")] long? Size);

        /// <summary>Every release-convention .bin asset across the repository's releases (drafts
        /// skipped). A release.yml-produced manifest.json asset, when present, supplies the SHA-256s;
        /// assets with any other name are ignored rather than guessed at.</summary>
        internal async Task<IReadOnlyList<RemoteFile>> FetchGitHubReleasesAsync(string repository, CancellationToken cancellationToken)
        {
            string json = await fetcher.GetStringAsync($"https://api.github.com/repos/{repository}/releases?per_page=100", gitHubApi: true, cancellationToken);
            List<GitHubRelease> releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, ManifestJson) ?? [];

            var files = new List<RemoteFile>();
            foreach (GitHubRelease release in releases.Where(r => !r.Draft))
            {
                var assets = release.Assets ?? [];
                Dictionary<string, string> sha = new(StringComparer.OrdinalIgnoreCase);
                GitHubAsset? manifestAsset = assets.FirstOrDefault(a => string.Equals(a.Name, ManifestFileName, StringComparison.OrdinalIgnoreCase));
                if (manifestAsset?.BrowserDownloadUrl != null)
                {
                    try
                    {
                        FirmwareManifest? manifest = JsonSerializer.Deserialize<FirmwareManifest>(
                            await fetcher.GetStringAsync(manifestAsset.BrowserDownloadUrl, gitHubApi: false, cancellationToken), ManifestJson);
                        foreach (var f in (manifest?.Releases ?? []).SelectMany(r => r.Files).Where(f => f.FileName != null && f.Sha256 != null))
                        {
                            sha[f.FileName!] = f.Sha256!;
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException)
                    {
                        log.LogWarning(ex, "Release {Tag}: manifest asset unreadable, continuing without checksums", release.TagName);
                    }
                }

                foreach (GitHubAsset asset in assets)
                {
                    if (asset.BrowserDownloadUrl == null || !FirmwareVersion.TryParseFileName(asset.Name, out string board, out string version))
                    {
                        continue;
                    }
                    files.Add(new RemoteFile(board, FirmwareVersion.Normalize(version)!, asset.Name!, asset.BrowserDownloadUrl, asset.Size,
                        sha.TryGetValue(asset.Name!, out var s) ? s : null, release.PublishedAt));
                }
            }
            return files;
        }

        internal async Task<IReadOnlyList<RemoteFile>> FetchCustomManifestAsync(string manifestUrl, CancellationToken cancellationToken)
        {
            FirmwareManifest? manifest = JsonSerializer.Deserialize<FirmwareManifest>(
                await fetcher.GetStringAsync(manifestUrl, gitHubApi: false, cancellationToken), ManifestJson);
            var baseUri = new Uri(manifestUrl);
            var files = new List<RemoteFile>();
            foreach (FirmwareManifestRelease release in manifest?.Releases ?? [])
            {
                foreach (FirmwareManifestFile file in release.Files)
                {
                    if (!FirmwareVersion.TryParseFileName(file.FileName, out string board, out string version))
                    {
                        continue;
                    }
                    // Relative (or absent - USB layout) URLs resolve against the manifest itself.
                    string url = new Uri(baseUri, file.Url ?? file.FileName!).ToString();
                    files.Add(new RemoteFile(board, FirmwareVersion.Normalize(version)!, file.FileName!, url, file.SizeBytes, file.Sha256, release.PublishedAt));
                }
            }
            return files;
        }
    }
}
