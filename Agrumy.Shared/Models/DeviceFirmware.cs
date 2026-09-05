namespace api.Models
{
    /// One catalog entry: a published build of one version for one board; the newest <see cref="Version"/> per <see cref="Board"/> (semver order) is offered unless <c>Device.FirmwareTargetVersion</c> pins a specific one. Rows with a null Board are legacy, matched by DeviceTypeID instead.
    public class DeviceFirmware
    {
        public int? IDDeviceFirmware { get; set; }

        /// Legacy key - kept so old rows still resolve; new rows leave it null.
        public int? DeviceTypeID { get; set; }

        /// PlatformIO environment name (esp32dev, esp32s3usbotg) - matches the Board the firmware reports in its config-poll heartbeat.
        public string? Board { get; set; }

        /// Semver string without the leading "v", e.g. "1.2.0". Column is varchar(20).
        public string? Version { get; set; }

        /// HTTP(S) URL the device downloads the .bin from during OTA.
        public string? Url { get; set; }

        /// Which source produced this row; offer/latest lookups only consider rows matching the active ServerConfig.FirmwareSource, plus Local rows always.
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSource Source { get; set; }

        /// File name in the release naming convention (agrumy-{board}-v{version}.bin). For Local rows this is also the name inside the API's storage directory.
        public string? FileName { get; set; }

        public long? SizeBytes { get; set; }

        /// Lower-case hex SHA-256, when known.
        public string? Sha256 { get; set; }

        public DateTime? PublishedAt { get; set; }
        public DateTime? DateAdded { get; set; }

        /// File name for the merged full-flash image (bootloader+partition+app), used by the blank-chip web installer; null if no such sibling was published for this row.
        public string? FullImageFileName { get; set; }

        /// Download URL for the full image, same sourcing rules as <see cref="Url"/>.
        public string? FullImageUrl { get; set; }

        public long? FullImageSizeBytes { get; set; }

        /// SHA-256 of the full image — distinct file, distinct hash from <see cref="Sha256"/> which covers only the OTA app binary.
        public string? FullImageSha256 { get; set; }
    }
}
