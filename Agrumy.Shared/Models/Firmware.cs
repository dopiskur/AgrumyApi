using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    /// <summary>Where the firmware catalog is populated from and where a device's OTA .bin is downloaded from: GitHub (public releases, zero-config default), Local (this API hosts the files, the only air-gapped-capable option), or Custom (operator-run repository serving the same manifest.json format).</summary>
    public enum FirmwareSource
    {
        GitHub = 0,
        Local = 1,
        Custom = 2,
    }

    /// <summary>What a "Pull from GitHub"/"Refresh" sync should do with rows the catalog already has.</summary>
    public enum FirmwareSyncMode
    {
        /// <summary>Re-read the active remote source and replace that source's catalog rows - nothing is downloaded, rows keep pointing at the remote URLs.</summary>
        Refresh = 0,
        /// <summary>Local repository: download every release .bin GitHub has that the local store does not, keep what is already there.</summary>
        PullIncremental = 1,
        /// <summary>Local repository: wipe every locally stored file + row first, then pull all.</summary>
        PullFull = 2,
    }

    /// <summary>Body of POST /api/Firmware/Sync.</summary>
    public class FirmwareSyncRequest
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSyncMode Mode { get; set; }
    }

    /// <summary>Body of POST /api/Firmware/Import: a directory on the API server (e.g. a mounted USB stick) holding .bin files in the release naming convention, optionally with a manifest.json.</summary>
    public class FirmwareImportRequest
    {
        public string? Path { get; set; }
    }

    /// <summary>Body of POST /api/Device/FirmwareUpdate: Version null = latest in the catalog for this device's board (one-click update); a specific version installs exactly that one (rollback/downgrade) and must exist in the catalog for the board.</summary>
    public class DeviceFirmwareUpdateRequest
    {
        public int IdDevice { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>Outcome summary of a sync/import/upload so the admin UI can say what actually
    /// happened rather than just "OK".</summary>
    public class FirmwareSyncResult
    {
        public int Added { get; set; }
        public int Skipped { get; set; }
        public int Removed { get; set; }
        public List<string> Warnings { get; set; } = [];
    }

    public class FirmwareManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime? GeneratedAt { get; set; }
        /// <summary>Free-text provenance, e.g. "github:dopiskur/AgrumyFirmware" or "local:api.agrumy.com".</summary>
        public string? Source { get; set; }
        public List<FirmwareManifestRelease> Releases { get; set; } = [];
    }

    public class FirmwareManifestRelease
    {
        public string? Version { get; set; }
        public DateTime? PublishedAt { get; set; }
        public List<FirmwareManifestFile> Files { get; set; } = [];
    }

    public class FirmwareManifestFile
    {
        /// <summary>PlatformIO environment name the .bin was built for - the same string the firmware reports as Board in its config-poll heartbeat.</summary>
        public string? Board { get; set; }
        public string? FileName { get; set; }
        public long? SizeBytes { get; set; }
        /// <summary>Lower-case hex SHA-256 of the .bin - verified on import and after each download.</summary>
        public string? Sha256 { get; set; }
        /// <summary>Absolute download URL, or null when the file sits next to the manifest (USB directory layout).</summary>
        public string? Url { get; set; }
        /// <summary>"ota" (default/absent) or "full" - the blank-chip-flashable merged image sibling of the "ota" row with the same Board+version. Anything but "full" is treated as OTA.</summary>
        public string? Kind { get; set; }
    }
}
