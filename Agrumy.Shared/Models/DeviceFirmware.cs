namespace api.Models
{
    /// <summary>
    /// A published firmware build for a device type (table <c>deviceFirmware</c>).
    /// The newest row per <see cref="DeviceTypeID"/> (by <see cref="DateAdded"/>) is what the
    /// API offers to a device whose <c>Device.FirmwareUpdate</c> flag is set - see
    /// <c>DeviceApiController.BuildDeviceConfigAsync</c> and roadmap #3.
    /// </summary>
    public class DeviceFirmware
    {
        public int? IDDeviceFirmware { get; set; }
        public int? DeviceTypeID { get; set; }

        /// <summary>Semver string, e.g. "0.1.1". Column is varchar(20) (was decimal(10,0)).</summary>
        public string? Version { get; set; }

        /// <summary>HTTP(S) URL the device downloads the .bin from during OTA.</summary>
        public string? Url { get; set; }

        public DateTime? DateAdded { get; set; }
    }
}
