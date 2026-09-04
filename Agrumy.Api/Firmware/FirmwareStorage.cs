using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace api.Firmware
{
    /// <summary>The directory this API serves .bin files from (Firmware:LocalPath, default "firmware-store" under the content root - NOT "firmware", which on a case-insensitive filesystem collides with this source folder when running from the project directory). File names are always the release convention, validated before anything is written or read, so a request path can never escape the directory.</summary>
    public sealed class FirmwareStorage(IOptions<AgrumySettings> settings, IHostEnvironment environment)
    {
        public const string DefaultRelativePath = "firmware-store";

        public string RootPath
        {
            get
            {
                string configured = string.IsNullOrWhiteSpace(settings.Value.FirmwareLocalPath) ? DefaultRelativePath : settings.Value.FirmwareLocalPath;
                return Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
            }
        }

        public string PathFor(string fileName)
        {
            // OTA and full-image conventions share this one flat directory - either is a "recognized, safe" name here; FirmwareCatalogService decides which is ALLOWED at each write path.
            if (!FirmwareVersion.TryParseFileName(fileName, out _, out _) &&
                !FirmwareVersion.TryParseFullImageFileName(fileName, out _, out _))
            {
                throw new ArgumentException($"'{fileName}' is not a release-convention firmware file name.", nameof(fileName));
            }
            return Path.Combine(RootPath, fileName);
        }

        public bool Exists(string fileName) => File.Exists(PathFor(fileName));

        /// <summary>Writes via a temp file + rename so a download that dies halfway never leaves a
        /// truncated .bin under the real name (the device would flash it - Update.end() would catch
        /// a short image, but only after the download wasted its cycle). Returns (size, sha256).</summary>
        public async Task<(long SizeBytes, string Sha256)> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
        {
            string finalPath = PathFor(fileName);
            Directory.CreateDirectory(RootPath);
            string tmpPath = finalPath + ".tmp";
            long size;
            string sha;
            try
            {
                await using (var file = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[81920];
                    int read;
                    size = 0;
                    while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        hasher.AppendData(buffer, 0, read);
                        size += read;
                    }
                    sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                }
            }
            catch
            {
                // Best-effort .tmp cleanup so a failed write doesn't leave dead weight on disk; own try/catch so a delete failure never masks the real exception.
                try { File.Delete(tmpPath); } catch { /* best-effort */ }
                throw;
            }
            File.Move(tmpPath, finalPath, overwrite: true);
            return (size, sha);
        }

        public void Delete(string fileName)
        {
            string path = PathFor(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
        {
            await using var file = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
