using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    /// <summary>Roadmap #94: where the firmware catalog (table deviceFirmware) is populated from
    /// and, for a device doing OTA (#3), where the .bin actually gets downloaded from. GitHub is the
    /// zero-config default (public AgrumyFirmware releases); Local means this API hosts the files
    /// itself (the only path that works air-gapped, and the escape hatch for a self-hosted install
    /// whose pinned servicePublicKey cert would never validate GitHub's TLS); Custom is an
    /// operator-run repository serving the same manifest.json format the offline-USB tools write.</summary>
    public enum FirmwareSource
    {
        GitHub = 0,
        Local = 1,
        Custom = 2,
    }

    /// <summary>Roadmap #94-2a: what a "Pull from GitHub"/"Refresh" sync should do with rows the
    /// catalog already has.</summary>
    public enum FirmwareSyncMode
    {
        /// <summary>Re-read the active remote source (GitHub Releases or the Custom manifest) and
        /// replace that source's catalog rows - nothing is downloaded, rows keep pointing at the
        /// remote URLs.</summary>
        Refresh = 0,
        /// <summary>Local repository: download every release .bin GitHub has that the local store
        /// does not, keep what is already there.</summary>
        PullIncremental = 1,
        /// <summary>Local repository: wipe every locally stored file + row first, then pull all.</summary>
        PullFull = 2,
    }

    /// <summary>Body of POST /api/Firmware/Sync.</summary>
    public class FirmwareSyncRequest
    {
        // See ServerConfig.FirmwareSource for why this is string-on-the-wire.
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSyncMode Mode { get; set; }
    }

    /// <summary>Body of POST /api/Firmware/Import (roadmap #94-2b): a directory ON THE API SERVER
    /// (e.g. a mounted USB stick) holding .bin files in the release naming convention, optionally
    /// with the manifest.json the offline-USB tools write next to them.</summary>
    public class FirmwareImportRequest
    {
        public string? Path { get; set; }
    }

    /// <summary>Body of POST /api/Device/FirmwareUpdate (roadmap #93): Version null = "latest in the
    /// catalog for this device's board" (one-click update); a specific version = install exactly
    /// that one (rollback/downgrade, #93-c-3) - it must exist in the catalog for the board.</summary>
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

    // ---- manifest.json: the one contract shared by Custom repositories, the offline-USB import
    // scanner (#94-2b) and both offline-USB preparation tools (#94-C1 browser button, #94-C2
    // script), and produced by the AgrumyFirmware release.yml workflow as a release asset. Kept flat
    // and versioned so a future field never breaks an older importer.

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
        /// <summary>PlatformIO environment name the .bin was built for (esp32dev, esp32s3usbotg) -
        /// the same string the firmware reports as Board in its config-poll heartbeat.</summary>
        public string? Board { get; set; }
        public string? FileName { get; set; }
        public long? SizeBytes { get; set; }
        /// <summary>Lower-case hex SHA-256 of the .bin - verified on import after a physical USB
        /// transfer, and by the browser tool after each download.</summary>
        public string? Sha256 { get; set; }
        /// <summary>Absolute download URL, or null when the file sits next to the manifest (USB
        /// directory layout).</summary>
        public string? Url { get; set; }
    }
}
