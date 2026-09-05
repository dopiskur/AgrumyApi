using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using api.Dal.Interface;
using api.Models;

namespace api.Firmware
{
    /// Business logic over the deviceFirmware catalog: active source, Local repository population (GitHub pull, directory import, manual upload), GitHub/Custom-manifest reads, and per-device offer resolution.
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

        // 50 pages * 100/page = 5000 releases - bounds FetchGitHubReleasesAsync's worst case.
        private const int MaxReleasePages = 50;

        /// The ACTIVE source's rows plus Local ones always - a manually uploaded .bin stays offerable even when GitHub is the selected default.
        public static IReadOnlyCollection<FirmwareSource> VisibleSources(FirmwareSource active) =>
            active == FirmwareSource.Local ? [FirmwareSource.Local] : [active, FirmwareSource.Local];

        public static string LocalDownloadUrl(string publicBaseUrl, string fileName) =>
            publicBaseUrl.TrimEnd('/') + "/api/Firmware/Download/" + fileName;

        // ---- reads -------------------------------------------------------------------------

        /// Whole catalog (every source, legacy rows included), board then newest version first.
        public async Task<IList<DeviceFirmware>> ListAsync()
        {
            IList<DeviceFirmware> rows = await firmwareRepo.FirmwareListAsync();
            return rows
                .OrderBy(r => r.Board ?? "~")
                .ThenByDescending(r => FirmwareVersion.TryParse(r.Version, out var v) ? v : default)
                .ToList();
        }

        /// Catalog entries a device on <paramref name="board"/> may be offered, newest first.
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

        /// The build to offer <paramref name="device"/>, or null: FirmwareUpdate must be set, a pinned FirmwareTargetVersion wins over "latest", and a Board-less device falls back to the legacy per-DeviceTypeID row.
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
                    // A pinned target is offered even without a checksum - RequestUpdateAsync already refused to pin one that lacks it, so reaching here with one means an operator explicitly wants it anyway (imported/legacy row).
                    ? candidates.FirstOrDefault(c => FirmwareVersion.AreEqual(c.Version, target))
                    // "latest" must mean latest the firmware will actually accept - OtaController.update refuses outright without a valid SHA-256, so offering a checksum-less build here is a silent dead end.
                    : candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c.Sha256));
                if (match != null)
                {
                    return match;
                }
            }

            return device.DeviceRoleID == null ? null : await deviceRepo.DeviceFirmwareLatestGetAsync(device.DeviceRoleID);
        }

        // ---- per-device update request ---------------------------------------

        /// Arms the OTA flag; returns an error message (for a 400) when the requested version isn't in the catalog for the device's board, or the device has no board reported yet.
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
            DeviceFirmware? candidate = candidates.FirstOrDefault(c => FirmwareVersion.AreEqual(c.Version, normalized));
            if (candidate == null)
            {
                return $"Version {normalized} is not in the catalog for board {board}.";
            }
            // OtaController.update refuses outright without a valid SHA-256 - a GitHub release with no manifest.json asset reaches the catalog this way, and arming the flag would just silently do nothing on every future poll.
            if (string.IsNullOrEmpty(candidate.Sha256))
            {
                return $"Version {normalized} has no SHA-256 checksum in the catalog - the device firmware refuses to install it without one.";
            }
            await firmwareRepo.DeviceFirmwareUpdateSetAsync(idDevice, true, normalized);
            return null;
        }

        public Task CancelUpdateAsync(int idDevice) => firmwareRepo.DeviceFirmwareUpdateSetAsync(idDevice, false, null);

        /// Called from GetConfig with the heartbeat - once the device reports the version it was asked to run, clears the flag so the UI stops showing "pending"; returns true on that transition.
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
            string repository = config.FirmwareGitHubRepository ?? "dopiskur/AgrumyFirmware";

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

            // PullFull / PullIncremental: GitHub -> local store, regardless of the currently active source - an admin prepares Local BEFORE switching to it.
            var result = new FirmwareSyncResult();
            if (mode == FirmwareSyncMode.PullFull)
            {
                // FullImageFileName is a sibling file on the same row - dropping only FileName orphaned it on disk.
                foreach (var row in (await firmwareRepo.FirmwareListAsync()).Where(r => r.Source == FirmwareSource.Local && r.FileName != null))
                {
                    storage.Delete(row.FileName!);
                    if (row.FullImageFileName != null)
                    {
                        storage.Delete(row.FullImageFileName);
                    }
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

                    // Pull the full-image sibling too, when published - a download failure here downgrades to a warning rather than aborting the row (the OTA half already succeeded).
                    string? fullImageFileName = null, fullImageSha = null;
                    long? fullImageSize = null;
                    if (remote.FullImageFileName != null && remote.FullImageUrl != null)
                    {
                        try
                        {
                            await using Stream fullContent = await fetcher.GetStreamAsync(remote.FullImageUrl, cancellationToken);
                            (long fiSize, string fiSha) = await storage.SaveAsync(remote.FullImageFileName, fullContent, cancellationToken);
                            if (remote.FullImageSha256 != null && !string.Equals(remote.FullImageSha256, fiSha, StringComparison.OrdinalIgnoreCase))
                            {
                                storage.Delete(remote.FullImageFileName);
                                result.Warnings.Add($"{remote.FullImageFileName}: SHA-256 mismatch against the release manifest - discarded.");
                            }
                            else
                            {
                                fullImageFileName = remote.FullImageFileName;
                                fullImageSize = fiSize;
                                fullImageSha = fiSha;
                            }
                        }
                        catch (Exception ex) when (ex is HttpRequestException or IOException)
                        {
                            log.LogWarning(ex, "Firmware pull of full-image {File} failed", remote.FullImageFileName);
                            result.Warnings.Add($"{remote.FullImageFileName}: download failed ({ex.Message}).");
                        }
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
                        FullImageFileName = fullImageFileName,
                        FullImageUrl = fullImageFileName == null ? null : LocalDownloadUrl(publicBaseUrl, fullImageFileName),
                        FullImageSizeBytes = fullImageSize,
                        FullImageSha256 = fullImageSha,
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
            // remoteFiles is already fully fetched/validated, so the only remaining risk was a DB error mid-loop - now one transaction.
            List<DeviceFirmware> rows = remoteFiles.Select(remote => new DeviceFirmware
            {
                Board = remote.Board,
                Version = remote.Version,
                FileName = remote.FileName,
                Url = remote.Url,
                Source = source,
                SizeBytes = remote.SizeBytes,
                Sha256 = remote.Sha256,
                PublishedAt = remote.PublishedAt,
                FullImageFileName = remote.FullImageFileName,
                FullImageUrl = remote.FullImageUrl,
                FullImageSizeBytes = remote.FullImageSizeBytes,
                FullImageSha256 = remote.FullImageSha256,
            }).ToList();

            int removed = await firmwareRepo.FirmwareReplaceSourceRowsAsync(source, rows);
            return new FirmwareSyncResult { Removed = removed, Added = rows.Count };
        }

        // ---- import a directory on this server (mounted USB) -----------------

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

            // Same ZIP extraction as UploadZipAsync (#337) - each .zip alongside loose .bin files in
            // the directory extracts to its own scratch dir, then imports through this same method,
            // so a directory can mix loose .bin files and .zip archives freely.
            foreach (string zipPath in Directory.EnumerateFiles(path, "*.zip"))
            {
                string zipTempDir = Path.Combine(Path.GetTempPath(), "agrumy-firmware-import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(zipTempDir);
                try
                {
                    await using FileStream zipContent = File.OpenRead(zipPath);
                    FirmwareSyncResult? rejection = ExtractZip(zipContent, zipTempDir);
                    if (rejection != null)
                    {
                        result.Warnings.Add($"{Path.GetFileName(zipPath)}: " + string.Join(' ', rejection.Warnings));
                        continue;
                    }
                    FirmwareSyncResult zipResult = await ImportFromDirectoryAsync(zipTempDir, publicBaseUrl, cancellationToken);
                    result.Added += zipResult.Added;
                    result.Skipped += zipResult.Skipped;
                    result.Warnings.AddRange(zipResult.Warnings.Select(w => $"{Path.GetFileName(zipPath)}: {w}"));
                }
                finally
                {
                    try { Directory.Delete(zipTempDir, recursive: true); } catch { /* best-effort cleanup */ }
                }
            }

            // Collected up front so the OTA loop below can look up a full-image sibling by (board, version) without a second directory scan.
            Dictionary<(string Board, string Version), string> fullImagePaths = new();
            foreach (string filePath in Directory.EnumerateFiles(path, "*.bin"))
            {
                string fileName = Path.GetFileName(filePath);
                if (FirmwareVersion.TryParseFullImageFileName(fileName, out string fiBoard, out string fiVersion))
                {
                    fullImagePaths[(fiBoard, FirmwareVersion.Normalize(fiVersion)!)] = filePath;
                }
            }

            foreach (string filePath in Directory.EnumerateFiles(path, "*.bin"))
            {
                string fileName = Path.GetFileName(filePath);
                if (!FirmwareVersion.TryParseFileName(fileName, out string board, out string version))
                {
                    if (!FirmwareVersion.TryParseFullImageFileName(fileName, out _, out _))
                    {
                        result.Warnings.Add($"{fileName}: not in the agrumy-<board>-v<version>.bin naming convention - skipped.");
                        result.Skipped++;
                    } // else: a full-image file, handled below alongside its OTA sibling (or silently skipped, see the warning after this loop).
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

                string? fullImagePath = fullImagePaths.GetValueOrDefault((board, FirmwareVersion.Normalize(version)!));
                if (fullImagePath != null)
                {
                    string fullImageFileName = Path.GetFileName(fullImagePath);
                    if (manifestSha.TryGetValue(fullImageFileName, out string? fiExpectedSha))
                    {
                        string fiActualSha = await FirmwareStorage.ComputeSha256Async(fullImagePath, cancellationToken);
                        if (!string.Equals(fiActualSha, fiExpectedSha, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Warnings.Add($"{fullImageFileName}: SHA-256 does not match {ManifestFileName} - imported OTA-only (full image discarded).");
                            fullImagePath = null;
                        }
                    }
                }

                await using FileStream content = File.OpenRead(filePath);
                if (fullImagePath != null)
                {
                    await using FileStream fullImageContent = File.OpenRead(fullImagePath);
                    await AddLocalAsync(board, version, fileName, content, publicBaseUrl, cancellationToken, Path.GetFileName(fullImagePath), fullImageContent);
                }
                else
                {
                    await AddLocalAsync(board, version, fileName, content, publicBaseUrl, cancellationToken);
                }
                result.Added++;
            }
            return result;
        }

        // ---- manual upload ---------------------------------------

        /// Returns the error (for a 400) when the file name is not in the convention.
        public async Task<(DeviceFirmware? Firmware, string? Error)> UploadAsync(string? fileName, Stream content, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            if (!FirmwareVersion.TryParseFileName(fileName, out string board, out string version))
            {
                return (null, $"'{fileName}' must be named agrumy-<board>-v<version>.bin (e.g. agrumy-esp32dev-v1.2.0.bin).");
            }
            return (await AddLocalAsync(board, version, fileName!, content, publicBaseUrl, cancellationToken), null);
        }

        /// <paramref name="fullImageFileName"/>/<paramref name="fullImageContent"/> are optional - only ImportFromDirectoryAsync supplies them; UploadAsync's single-file form stays OTA-only.
        private async Task<DeviceFirmware> AddLocalAsync(string board, string version, string fileName, Stream content, string publicBaseUrl,
            CancellationToken cancellationToken, string? fullImageFileName = null, Stream? fullImageContent = null)
        {
            (long size, string sha) = await storage.SaveAsync(fileName, content, cancellationToken);
            (long fiSize, string fiSha)? fullImage = fullImageFileName != null && fullImageContent != null
                ? await storage.SaveAsync(fullImageFileName, fullImageContent, cancellationToken)
                : null;

            // Same board+version already stored locally: the stale row (old size/sha) must go too - one row per (board, version, source).
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
                FullImageFileName = fullImage == null ? null : fullImageFileName,
                FullImageUrl = fullImage == null ? null : LocalDownloadUrl(publicBaseUrl, fullImageFileName!),
                FullImageSizeBytes = fullImage?.fiSize,
                FullImageSha256 = fullImage?.fiSha,
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
            // FullImageFileName is a sibling file on the same row - dropping only FileName left it orphaned on disk.
            if (row.Source == FirmwareSource.Local)
            {
                if (row.FileName != null)
                {
                    storage.Delete(row.FileName);
                }
                if (row.FullImageFileName != null)
                {
                    storage.Delete(row.FullImageFileName);
                }
            }
            await firmwareRepo.FirmwareDeleteAsync(idDeviceFirmware);
            return true;
        }

        // ---- ZIP download/upload (Web admin page's "Local Repository" tab) --------

        // Bounds UploadZipAsync's extraction against a crafted ZIP claiming a tiny compressed size but a huge uncompressed one.
        private const int MaxZipEntries = 64;
        private const long MaxZipUncompressedBytes = 200L * 1024 * 1024;

        /// A downloadable ZIP of the visible catalog (active source + Local) plus its manifest.json - <paramref name="latestOnly"/> keeps just the newest file per (Board, Kind), otherwise every visible file is included; round-trips through <see cref="UploadZipAsync"/>.
        public async Task<(Stream Content, string FileName)> BuildDownloadZipAsync(bool latestOnly, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            FirmwareManifest manifest = await BuildManifestAsync(publicBaseUrl);
            var selected = new List<(FirmwareManifestRelease Release, FirmwareManifestFile File)>();
            var seenBoardKind = new HashSet<(string Board, string Kind)>();
            foreach (FirmwareManifestRelease release in manifest.Releases) // already newest-version-first
            {
                foreach (FirmwareManifestFile file in release.Files)
                {
                    if (file.Board == null)
                    {
                        continue;
                    }
                    if (latestOnly && !seenBoardKind.Add((file.Board, file.Kind ?? "ota")))
                    {
                        continue;
                    }
                    selected.Add((release, file));
                }
            }

            var trimmedManifest = new FirmwareManifest
            {
                GeneratedAt = manifest.GeneratedAt,
                Source = manifest.Source,
                Releases = selected
                    .GroupBy(s => s.Release.Version)
                    .Select(g => new FirmwareManifestRelease { Version = g.Key, PublishedAt = g.First().Release.PublishedAt, Files = g.Select(s => s.File).ToList() })
                    .ToList(),
            };

            var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (Stream manifestEntry = zip.CreateEntry(ManifestFileName, CompressionLevel.Optimal).Open())
                {
                    await JsonSerializer.SerializeAsync(manifestEntry, trimmedManifest, ManifestJson, cancellationToken);
                }
                var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (_, file) in selected)
                {
                    if (file.FileName == null || !addedFiles.Add(file.FileName))
                    {
                        continue;
                    }
                    var opened = await OpenAsync(file.FileName, cancellationToken);
                    if (opened == null)
                    {
                        continue;
                    }
                    await using Stream source = opened.Value.Content;
                    await using Stream dest = zip.CreateEntry(file.FileName, CompressionLevel.Optimal).Open();
                    await source.CopyToAsync(dest, cancellationToken);
                }
            }
            zipStream.Position = 0;
            string fileName = $"agrumy-firmware-{(latestOnly ? "latest" : "all")}-{DateTime.UtcNow:yyyyMMdd}.zip";
            return (zipStream, fileName);
        }

        /// Extracts a ZIP built by <see cref="BuildDownloadZipAsync"/> (or hand-assembled the same way) to a scratch directory and imports it exactly like <see cref="ImportFromDirectoryAsync"/> - same manifest.json SHA verification, same full-image pairing, no separate code path to drift.
        public async Task<FirmwareSyncResult> UploadZipAsync(Stream zipContent, string publicBaseUrl, CancellationToken cancellationToken = default)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "agrumy-firmware-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                FirmwareSyncResult? rejection = ExtractZip(zipContent, tempDir);
                if (rejection != null)
                {
                    return rejection;
                }
                return await ImportFromDirectoryAsync(tempDir, publicBaseUrl, cancellationToken);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }

        /// Shared by UploadZipAsync and ImportFromDirectoryAsync's own .zip handling (#337) - extracts
        /// into destDir (already created by the caller) and returns null on success, or a
        /// warnings-only FirmwareSyncResult if the archive was rejected outright (too many entries/too
        /// large uncompressed).
        private static FirmwareSyncResult? ExtractZip(Stream zipContent, string destDir)
        {
            using var archive = new ZipArchive(zipContent, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxZipEntries)
            {
                return new FirmwareSyncResult { Warnings = { $"ZIP has {archive.Entries.Count} entries, more than the {MaxZipEntries} allowed - rejected." } };
            }
            long totalUncompressed = archive.Entries.Sum(e => e.Length);
            if (totalUncompressed > MaxZipUncompressedBytes)
            {
                return new FirmwareSyncResult { Warnings = { $"ZIP would extract to more than {MaxZipUncompressedBytes / 1024 / 1024} MB - rejected." } };
            }
            // Defense in depth beyond .NET's own entry-name checks - an entry can never resolve outside destDir.
            string destDirFull = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // directory entry
                }
                string destination = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!destination.StartsWith(destDirFull, StringComparison.Ordinal))
                {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
            return null;
        }

        // ---- manifest + file access (ZIP download, Custom repositories) --------

        /// The visible catalog in manifest.json form - embedded in BuildDownloadZipAsync's ZIP and what another Agrumy install reads as a Custom repository.
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
                List<DeviceFirmware> boardRows = group
                    .GroupBy(r => r.Board!) // a board present from two sources: one entry, Local preferred (served by this host)
                    .Select(b => b.OrderBy(r => r.Source == FirmwareSource.Local ? 0 : 1).First())
                    .ToList();
                var files = new List<FirmwareManifestFile>();
                foreach (DeviceFirmware r in boardRows)
                {
                    files.Add(new FirmwareManifestFile { Board = r.Board, FileName = r.FileName, SizeBytes = r.SizeBytes, Sha256 = r.Sha256, Url = r.Url, Kind = "ota" });
                    // A second manifest entry, not a field on the OTA one, so a flat-file-list-only reader still gets a correct OTA-only picture.
                    if (r.FullImageFileName != null)
                    {
                        files.Add(new FirmwareManifestFile { Board = r.Board, FileName = r.FullImageFileName, SizeBytes = r.FullImageSizeBytes, Sha256 = r.FullImageSha256, Url = r.FullImageUrl, Kind = "full" });
                    }
                }
                manifest.Releases.Add(new FirmwareManifestRelease
                {
                    Version = group.Key,
                    PublishedAt = group.Max(r => r.PublishedAt),
                    Files = files,
                });
            }
            return manifest;
        }

        /// Opens a catalog entry's bytes wherever they live - local store, or streamed through from the remote URL so the browser tool never fetches GitHub cross-origin; keyed by file name, Local preferred.
        public async Task<(Stream Content, string FileName)?> OpenAsync(string? fileName, CancellationToken cancellationToken = default)
        {
            // A full-image file name lives on FullImageFileName, never FileName - this must know which field to match before it can find or open anything.
            bool isFullImage = FirmwareVersion.TryParseFullImageFileName(fileName, out string board, out _);
            if (!isFullImage && !FirmwareVersion.TryParseFileName(fileName, out board, out _))
            {
                return null;
            }
            FirmwareSource active = (await configRepo.ServerConfigGetAsync(1)).FirmwareSource;
            DeviceFirmware? row = (await firmwareRepo.FirmwareListForBoardAsync(board, VisibleSources(active)))
                .Where(r => string.Equals(isFullImage ? r.FullImageFileName : r.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Source == FirmwareSource.Local ? 0 : 1)
                .FirstOrDefault();
            if (row == null)
            {
                return null;
            }
            string? url = isFullImage ? row.FullImageUrl : row.Url;
            if (row.Source == FirmwareSource.Local)
            {
                return storage.Exists(fileName!) ? (File.OpenRead(storage.PathFor(fileName!)), fileName!) : null;
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            return (await fetcher.GetStreamAsync(url, cancellationToken), fileName!);
        }

        // ---- remote readers ------------------------------------------------------------------

        /// FullImage* fields are the paired blank-chip image for this same Board+Version, when the remote source published one - null when it didn't.
        internal sealed record RemoteFile(string Board, string Version, string FileName, string Url, long? SizeBytes, string? Sha256, DateTime? PublishedAt,
            string? FullImageFileName = null, string? FullImageUrl = null, long? FullImageSizeBytes = null, string? FullImageSha256 = null);

        private sealed record GitHubRelease(
            [property: JsonPropertyName("tag_name")] string? TagName,
            [property: JsonPropertyName("draft")] bool Draft,
            [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
            [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

        private sealed record GitHubAsset(
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
            [property: JsonPropertyName("size")] long? Size);

        /// Every release-convention .bin asset across the repository's releases (drafts skipped); a manifest.json asset, when present, supplies the SHA-256s, other names are ignored rather than guessed at.
        internal async Task<IReadOnlyList<RemoteFile>> FetchGitHubReleasesAsync(string repository, CancellationToken cancellationToken)
        {
            var releases = new List<GitHubRelease>();
            // Page until a page comes back short of 100 - GitHub's own signal that it was the last one.
            for (int page = 1; page <= MaxReleasePages; page++)
            {
                string json = await fetcher.GetStringAsync($"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}", gitHubApi: true, cancellationToken);
                List<GitHubRelease>? pageReleases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, ManifestJson);
                if (pageReleases == null || pageReleases.Count == 0)
                {
                    break;
                }
                releases.AddRange(pageReleases);
                if (pageReleases.Count < 100)
                {
                    break; // short page - this was the last one, skip the extra request that would just come back empty
                }
            }

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

                // Two passes over the asset list: OTA first (TryParseFileName never matches "-full-v..."), then full-image assets matched onto their same-board+version OTA RemoteFile.
                var releaseFiles = new List<RemoteFile>();
                foreach (GitHubAsset asset in assets)
                {
                    if (asset.BrowserDownloadUrl == null || !FirmwareVersion.TryParseFileName(asset.Name, out string board, out string version))
                    {
                        continue;
                    }
                    releaseFiles.Add(new RemoteFile(board, FirmwareVersion.Normalize(version)!, asset.Name!, asset.BrowserDownloadUrl, asset.Size,
                        sha.TryGetValue(asset.Name!, out var s) ? s : null, release.PublishedAt));
                }
                foreach (GitHubAsset asset in assets)
                {
                    if (asset.BrowserDownloadUrl == null || !FirmwareVersion.TryParseFullImageFileName(asset.Name, out string board, out string version))
                    {
                        continue;
                    }
                    string normalized = FirmwareVersion.Normalize(version)!;
                    int i = releaseFiles.FindIndex(f => f.Board == board && FirmwareVersion.AreEqual(f.Version, normalized));
                    if (i < 0)
                    {
                        log.LogWarning("Release {Tag}: full-image asset {Asset} has no matching OTA asset for {Board} {Version} - ignored",
                            release.TagName, asset.Name, board, normalized);
                        continue;
                    }
                    releaseFiles[i] = releaseFiles[i] with
                    {
                        FullImageFileName = asset.Name,
                        FullImageUrl = asset.BrowserDownloadUrl,
                        FullImageSizeBytes = asset.Size,
                        FullImageSha256 = sha.TryGetValue(asset.Name!, out var fs) ? fs : null,
                    };
                }
                files.AddRange(releaseFiles);
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
                // Same two-pass shape as FetchGitHubReleasesAsync - OTA vs full-image is decided by FILE NAME convention, not the (possibly missing/wrong) Kind field.
                var releaseFiles = new List<RemoteFile>();
                foreach (FirmwareManifestFile file in release.Files)
                {
                    if (!FirmwareVersion.TryParseFileName(file.FileName, out string board, out string version))
                    {
                        continue;
                    }
                    // Relative (or absent - USB layout) URLs resolve against the manifest itself.
                    string url = new Uri(baseUri, file.Url ?? file.FileName!).ToString();
                    releaseFiles.Add(new RemoteFile(board, FirmwareVersion.Normalize(version)!, file.FileName!, url, file.SizeBytes, file.Sha256, release.PublishedAt));
                }
                foreach (FirmwareManifestFile file in release.Files)
                {
                    if (!FirmwareVersion.TryParseFullImageFileName(file.FileName, out string board, out string version))
                    {
                        continue;
                    }
                    string normalized = FirmwareVersion.Normalize(version)!;
                    int i = releaseFiles.FindIndex(f => f.Board == board && FirmwareVersion.AreEqual(f.Version, normalized));
                    if (i < 0)
                    {
                        continue; // no matching OTA entry in this manifest - ignore, same as GitHub's path.
                    }
                    string url = new Uri(baseUri, file.Url ?? file.FileName!).ToString();
                    releaseFiles[i] = releaseFiles[i] with
                    {
                        FullImageFileName = file.FileName,
                        FullImageUrl = url,
                        FullImageSizeBytes = file.SizeBytes,
                        FullImageSha256 = file.Sha256,
                    };
                }
                files.AddRange(releaseFiles);
            }
            return files;
        }
    }
}
