using System.Text.RegularExpressions;

namespace api.Firmware
{
    /// Semver ordering for catalog versions and the release file naming convention, in one place - a string sort would wrongly put "1.10.0" before "1.9.0".
    public readonly partial record struct FirmwareVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<FirmwareVersion>
    {
        /// agrumy-{board}-v{version}.bin - what AgrumyFirmware's release.yml produces, what the GitHub/Custom syncs accept, and what the import scanner/upload validate before a file enters the catalog.
        [GeneratedRegex(@"^agrumy-(?<board>[a-z0-9]+)-v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?)\.bin$")]
        private static partial Regex FileNameRegex();

        [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z][0-9A-Za-z.-]*))?$")]
        private static partial Regex VersionRegex();

        /// `git describe --tags --dirty`'s suffix format (firmware_version.py's non-release fallback) - despite looking like a semver pre-release after the "-", it means commits AFTER the tag, not before it.
        [GeneratedRegex(@"^\d+-g[0-9a-f]+(-dirty)?$")]
        private static partial Regex GitDescribeCommitSuffixRegex();

        /// agrumy-{board}-full-v{version}.bin, the blank-chip web-installer image (bootloader+partition table+boot_app0+OTA app merged to one flashable file) - "full-" sits BEFORE "v" so it can't be swallowed as a bogus pre-release tail by FileNameRegex.
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

        /// Canonical form without a leading "v" - the catalog/heartbeat wire form.
        public static string? Normalize(string? text) => TryParse(text, out var v) ? v.ToString() : null;

        /// True when <paramref name="candidate"/> is a valid version strictly newer than <paramref name="running"/> - an unparseable running version counts as older than any real release, so an unknown build gets offered the latest.
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
            // Semver: a pre-release sorts BEFORE the release ("1.2.0-rc1" < "1.2.0") EXCEPT a git-describe commit-count suffix ("1.2.0-3-gabc1234"), which sorts AFTER it - commits past the tag, not a preview build.
            if (PreRelease == null && other.PreRelease == null) { return 0; }
            if (PreRelease == null) { return GitDescribeCommitSuffixRegex().IsMatch(other.PreRelease!) ? -1 : 1; }
            if (other.PreRelease == null) { return GitDescribeCommitSuffixRegex().IsMatch(PreRelease!) ? 1 : -1; }
            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public override string ToString() => PreRelease == null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

        /// Splits a release-convention file name into its board and version, or returns false for anything that isn't one.
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

        /// Counterpart of <see cref="TryParseFileName"/> - matches only the full-image convention, never the plain OTA one.
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
