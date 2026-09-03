using api.Firmware;

namespace Agrumy.Api.Tests;

/// <summary>Roadmap #94: semver ordering + the release file naming convention (pure, no I/O).</summary>
public class FirmwareVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("0.1.4", 0, 1, 4, null)]
    [InlineData("2.0.0-rc.1", 2, 0, 0, "rc.1")]
    public void Parses_Semver_With_Optional_v_And_PreRelease(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(FirmwareVersion.TryParse(text, out var v));
        Assert.Equal((major, minor, patch, pre), (v.Major, v.Minor, v.Patch, v.PreRelease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("latest")]
    [InlineData("0.0.0-dev+dirty!")]
    public void Rejects_NonSemver(string text) => Assert.False(FirmwareVersion.IsValid(text));

    [Fact]
    public void Orders_Numerically_Not_Lexically()
    {
        // The whole reason DateAdded/string order was replaced: "1.10.0" must sort after "1.9.0".
        Assert.True(FirmwareVersion.IsNewer("1.10.0", "1.9.0"));
        Assert.False(FirmwareVersion.IsNewer("1.9.0", "1.10.0"));
        Assert.False(FirmwareVersion.IsNewer("1.9.0", "1.9.0"));
    }

    [Fact]
    public void PreRelease_Sorts_Before_The_Same_Release()
    {
        Assert.True(FirmwareVersion.IsNewer("1.2.0", "1.2.0-rc1"));
        Assert.False(FirmwareVersion.IsNewer("1.2.0-rc1", "1.2.0"));
    }

    [Fact]
    public void Unparseable_Running_Version_Counts_As_Older_Than_Any_Release()
    {
        // A dev build ("0.0.0-dev-abc1234" from git describe fallback, or garbage) must still be
        // offered the latest release rather than be stuck forever.
        Assert.True(FirmwareVersion.IsNewer("1.0.0", "garbage"));
        Assert.True(FirmwareVersion.IsNewer("1.0.0", null));
        Assert.False(FirmwareVersion.IsNewer(null, "1.0.0"));
        Assert.False(FirmwareVersion.IsNewer("garbage", "1.0.0"));
    }

    [Fact]
    public void AreEqual_Ignores_Leading_v()
    {
        Assert.True(FirmwareVersion.AreEqual("v1.2.0", "1.2.0"));
        Assert.False(FirmwareVersion.AreEqual("1.2.0", "1.2.1"));
        Assert.False(FirmwareVersion.AreEqual(null, "1.2.1"));
    }

    [Theory]
    [InlineData("agrumy-esp32dev-v1.2.0.bin", "esp32dev", "1.2.0")]
    [InlineData("agrumy-esp32s3usbotg-v0.3.1-rc1.bin", "esp32s3usbotg", "0.3.1-rc1")]
    public void FileName_Convention_Parses(string name, string board, string version)
    {
        Assert.True(FirmwareVersion.TryParseFileName(name, out var b, out var v));
        Assert.Equal(board, b);
        Assert.Equal(version, v);
    }

    [Theory]
    [InlineData("firmware.bin")]
    [InlineData("agrumy-esp32dev-1.2.0.bin")]       // missing v
    [InlineData("agrumy-esp32dev-v1.2.0.bin.tmp")]
    [InlineData("../agrumy-esp32dev-v1.2.0.bin")]   // path traversal attempt
    [InlineData("agrumy-ESP32DEV-v1.2.0.bin")]      // boards are lower-case env names
    [InlineData("")]
    [InlineData(null)]
    public void FileName_Convention_Rejects_Everything_Else(string? name) =>
        Assert.False(FirmwareVersion.TryParseFileName(name, out _, out _));

    [Fact]
    public void BuildFileName_RoundTrips() =>
        Assert.Equal("agrumy-esp32dev-v1.2.0.bin", FirmwareVersion.BuildFileName("esp32dev", "v1.2.0"));

    // ---- roadmap #41: full-image (blank-chip web installer) convention -----------------------

    [Theory]
    [InlineData("agrumy-esp32dev-full-v1.2.0.bin", "esp32dev", "1.2.0")]
    [InlineData("agrumy-esp32s3usbotg-full-v0.3.1-rc1.bin", "esp32s3usbotg", "0.3.1-rc1")]
    public void FullImageFileName_Convention_Parses(string name, string board, string version)
    {
        Assert.True(FirmwareVersion.TryParseFullImageFileName(name, out var b, out var v));
        Assert.Equal(board, b);
        Assert.Equal(version, v);
    }

    [Fact]
    public void BuildFullImageFileName_RoundTrips() =>
        Assert.Equal("agrumy-esp32dev-full-v1.2.0.bin", FirmwareVersion.BuildFullImageFileName("esp32dev", "v1.2.0"));

    /// <summary>The whole reason the "-full-" marker sits BEFORE "v" instead of being appended after
    /// the version: an after-the-version suffix would collide with FileNameRegex's own pre-release
    /// grammar (which already accepts an arbitrary "-something" tail) and get silently parsed as a
    /// version like "1.2.3-full" instead of being rejected. Each convention's regex must reject the
    /// other's file names outright, not just "usually" disambiguate them.</summary>
    [Fact]
    public void OtaAndFullImage_Conventions_Never_Match_Each_Others_FileName()
    {
        const string ota = "agrumy-esp32dev-v1.2.0.bin";
        const string full = "agrumy-esp32dev-full-v1.2.0.bin";

        Assert.True(FirmwareVersion.TryParseFileName(ota, out _, out _));
        Assert.False(FirmwareVersion.TryParseFullImageFileName(ota, out _, out _));

        Assert.True(FirmwareVersion.TryParseFullImageFileName(full, out _, out _));
        Assert.False(FirmwareVersion.TryParseFileName(full, out _, out _));
    }
}
