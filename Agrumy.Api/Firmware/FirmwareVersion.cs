using System.Text.RegularExpressions;

namespace api.Firmware
{
    /// <summary>Semver ordering for catalog versions and the release file naming convention, in one place - a string sort puts "1.10.0" before "1.9.0". Pure, no I/O, so it is unit-tested directly.</summary>
    public readonly partial record struct FirmwareVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<FirmwareVersion>
    {
        /// <summary>agrumy-{board}-v{version}.bin - what AgrumyFirmware's release.yml produces, what the GitHub/Custom syncs accept, and what the import scanner/upload validate before a file enters the catalog.</summary>
        [GeneratedRegex(@"^agrumy-(?<board>[a-z0-9]+)-v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?)\.bin$")]
        private static partial Regex FileNameRegex();

        [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z][0-9A-Za-z.-]*))?$")]
        private static partial Regex VersionRegex();

        /// <summary>agrumy-{board}-full-v{version}.bin - the blank-chip web-installer image (bootloader + partition table + boot_app0 + the OTA app, merged to one file flashable at offset 0). The "full-" marker sits BEFORE "v", not appended after the version: FileNameRegex's pre-release group already accepts an arbitrary "-something" tail on the version, so a suffix-after-version convention would silently parse as a version like "1.2.3-full" instead of being rejected.</summary>
        [GeneratedRegex(@"^agrumy-(?<board>[a-z0-9]+)-full-v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?)\.bin$")]
        private static partial Regex FullImageFileNameRegex();

        public static bool TryParse(string? text, out FirmwareVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            Match m = VersionRegex().Match(text.Trim());
            if (!m.Success)
            {
                return false;
            }
            version = new FirmwareVersion(
                int.Parse(m.Groups["major"].Value),
                int.Parse(m.Groups["minor"].Value),
                int.Parse(m.Groups["patch"].Value),
                m.Groups["pre"].Success ? m.Groups["pre"].Value : null);
            return true;
        }

        public static FirmwareVersion Parse(string text) =>
            TryParse(text, out var v) ? v : throw new FormatException($"Not a firmware version: '{text}'");

        public static bool IsValid(string? text) => TryParse(text, out _);

        /// <summary>Canonical form without a leading "v" - the catalog/heartbeat wire form.</summary>
        public static string? Normalize(string? text) => TryParse(text, out var v) ? v.ToString() : null;

        /// <summary>True when <paramref name="candidate"/> is a valid version strictly newer than <paramref name="running"/>. An unparseable running version counts as older than any real release, so a device on an unknown build is offered the latest rather than never updated.</summary>
        public static bool IsNewer(string? candidate, string? running)
        {
            if (!TryParse(candidate, out var c))
            {
                return false;
            }
            return !TryParse(running, out var r) || c.CompareTo(r) > 0;
        }

        public static bool AreEqual(string? a, string? b) =>
            TryParse(a, out var va) && TryParse(b, out var vb) && va.CompareTo(vb) == 0;

        public int CompareTo(FirmwareVersion other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) { return c; }
            c = Minor.CompareTo(other.Minor);
            if (c != 0) { return c; }
            c = Patch.CompareTo(other.Patch);
            if (c != 0) { return c; }
            // Semver rule: a pre-release sorts BEFORE the same release ("1.2.0-rc1" < "1.2.0").
            if (PreRelease == null && other.PreRelease == null) { return 0; }
            if (PreRelease == null) { return 1; }
            if (other.PreRelease == null) { return -1; }
            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public override string ToString() => PreRelease == null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

        /// <summary>Splits a release-convention file name into its board and version, or returns
        /// false for anything that isn't one.</summary>
        public static bool TryParseFileName(string? fileName, out string board, out string version)
        {
            board = "";
            version = "";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }
            Match m = FileNameRegex().Match(fileName.Trim());
            if (!m.Success)
            {
                return false;
            }
            board = m.Groups["board"].Value;
            version = m.Groups["version"].Value;
            return true;
        }

        public static string BuildFileName(string board, string version) => $"agrumy-{board}-v{Normalize(version) ?? version}.bin";

        /// <summary>Counterpart of <see cref="TryParseFileName"/> - matches only the full-image convention, never the plain OTA one (and vice versa).</summary>
        public static bool TryParseFullImageFileName(string? fileName, out string board, out string version)
        {
            board = "";
            version = "";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }
            Match m = FullImageFileNameRegex().Match(fileName.Trim());
            if (!m.Success)
            {
                return false;
            }
            board = m.Groups["board"].Value;
            version = m.Groups["version"].Value;
            return true;
        }

        public static string BuildFullImageFileName(string board, string version) => $"agrumy-{board}-full-v{Normalize(version) ?? version}.bin";
    }
}
