namespace api.Utils
{
    /// <summary>Roadmap #41: esp-web-tools' manifest format needs a "chipFamily" per build so it can
    /// refuse to flash a board's image onto the wrong physical chip - nothing in Agrumy.Shared tracks
    /// this today (Board is a free-form PlatformIO environment name everywhere else), so it lives here,
    /// scoped to the one place that actually needs it.</summary>
    internal static class EspChipFamily
    {
        // Keep in sync with AgrumyFirmware's platformio.ini [env:*] sections - board_build.mcu (or its
        // absence, meaning the classic ESP32) is what decides the value on the CI/merge_bin side too.
        private static readonly Dictionary<string, string> ByBoard = new(StringComparer.OrdinalIgnoreCase)
        {
            ["esp32dev"] = "ESP32",
            ["esp32s3usbotg"] = "ESP32-S3",
            // Roadmap #149: same chip family as esp32dev/esp32s3usbotg respectively (different
            // physical board, same MCU) - deliberately NOT yet in release.yml's build matrix
            // (unverified on real hardware), so these never actually appear in the catalog today.
            // Listed here anyway so FirmwareController's chip-family grouping (roadmap #148) treats
            // them correctly the moment they do: a family with more than one catalog board falls
            // back to manual per-board buttons instead of guessing.
            ["kc868-a6"] = "ESP32",
            ["esp32-s3-relay-6ch"] = "ESP32-S3",
        };

        public static string? ForBoard(string? board) => board != null && ByBoard.TryGetValue(board, out string? family) ? family : null;
    }
}
