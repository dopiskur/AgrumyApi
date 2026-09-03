namespace api.Utils
{
    internal static class EspChipFamily
    {
        // Keep in sync with AgrumyFirmware's platformio.ini [env:*] board_build.mcu (absence means classic ESP32).
        private static readonly Dictionary<string, string> ByBoard = new(StringComparer.OrdinalIgnoreCase)
        {
            ["esp32dev"] = "ESP32",
            ["esp32s3usbotg"] = "ESP32-S3",
            // Not yet in release.yml's build matrix / unverified on real hardware; listed so a future catalog entry gets the right chipFamily.
            ["kc868-a6"] = "ESP32",
            ["esp32-s3-relay-6ch"] = "ESP32-S3",
        };

        public static string? ForBoard(string? board) => board != null && ByBoard.TryGetValue(board, out string? family) ? family : null;
    }
}
