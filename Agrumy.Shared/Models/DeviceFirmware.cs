namespace api.Models
{
    /// <summary>
    /// One catalog entry (table <c>deviceFirmware</c>): a published build of one version for one
    /// board. Roadmap #94 turned this from a hand-inserted per-DeviceTypeID row into a real catalog
    /// populated from a <see cref="FirmwareSource"/> - the newest <see cref="Version"/> per
    /// <see cref="Board"/> (semver order, see api.Firmware.FirmwareVersion) is what a device with
    /// <c>Device.FirmwareUpdate</c> set is offered, unless <c>Device.FirmwareTargetVersion</c> pins
    /// a specific one (roadmap #93 rollback). Rows with a null Board are pre-#94 legacy entries
    /// still matched by DeviceTypeID for a device whose firmware predates the Board heartbeat field.
    /// </summary>
    public class DeviceFirmware
    {
        public int? IDDeviceFirmware { get; set; }

        /// <summary>Legacy (pre-#94) key - kept so old rows still resolve; new rows leave it null.</summary>
        public int? DeviceTypeID { get; set; }

        /// <summary>PlatformIO environment name (esp32dev, esp32s3usbotg) - matches the Board the
        /// firmware reports in its config-poll heartbeat.</summary>
        public string? Board { get; set; }

        /// <summary>Semver string without the leading "v", e.g. "1.2.0". Column is varchar(20).</summary>
        public string? Version { get; set; }

        /// <summary>HTTP(S) URL the device downloads the .bin from during OTA - a GitHub release
        /// asset, a Custom repository file, or this API's own /api/Firmware/Download for Local.</summary>
        public string? Url { get; set; }

        /// <summary>Which source put this row here - the offer/latest lookups only consider rows of
        /// the ACTIVE ServerConfig.FirmwareSource plus Local ones (always servable by this API).</summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSource Source { get; set; }

        /// <summary>File name in the release naming convention (agrumy-{board}-v{version}.bin).
        /// For Local rows this is also the name inside the API's storage directory.</summary>
        public string? FileName { get; set; }

        public long? SizeBytes { get; set; }

        /// <summary>Lower-case hex SHA-256, when known (release manifest, computed on import/upload).</summary>
        public string? Sha256 { get; set; }

        public DateTime? PublishedAt { get; set; }
        public DateTime? DateAdded { get; set; }

        // ---- roadmap #41: blank-chip web installer -----------------------------------------
        // A merged image (bootloader + partition table + boot_app0 + this row's own OTA app
        // binary, all at their real flash offsets, offset 0 once merged) published ALONGSIDE the
        // OTA file above by the same release - see AgrumyFirmware release.yml's merge_bin step and
        // api.Firmware.FirmwareVersion.TryParseFullImageFileName (agrumy-{board}-full-v{version}.bin).
        // Null when this board+version predates #41 or no such sibling was published - the web
        // installer simply has nothing to offer for that row, OTA is unaffected either way.

        /// <summary>File name in the full-image naming convention. For Local rows this is also the
        /// name inside the API's storage directory, alongside <see cref="FileName"/>.</summary>
        public string? FullImageFileName { get; set; }

        /// <summary>Download URL for the full image, same sourcing rules as <see cref="Url"/>.</summary>
        public string? FullImageUrl { get; set; }

        public long? FullImageSizeBytes { get; set; }

        /// <summary>Lower-case hex SHA-256 of the full image (a distinct file, distinct hash from
        /// <see cref="Sha256"/> which covers only the OTA app binary).</summary>
        public string? FullImageSha256 { get; set; }
    }
}
