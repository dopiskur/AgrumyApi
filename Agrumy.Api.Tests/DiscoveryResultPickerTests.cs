using api.Models;
using api.Utils;

namespace Agrumy.Api.Tests;

public class DiscoveryResultPickerTests
{
    private static DiscoveryResult Report(string mac, int scanningDeviceId, int? rssi) => new()
    {
        DiscoveredApMac = mac,
        ScanningDeviceID = scanningDeviceId,
        Rssi = rssi,
    };

    [Fact]
    public void Pick_HigherRssiWins()
    {
        var result = DiscoveryResultPicker.Pick([
            Report("AA:BB", scanningDeviceId: 1, rssi: -70),
            Report("AA:BB", scanningDeviceId: 2, rssi: -50),
        ]);

        var winner = Assert.Single(result);
        Assert.Equal(2, winner.ScanningDeviceID);
    }

    [Fact]
    public void Pick_EqualRssi_HigherScanningDeviceIdWins()
    {
        var result = DiscoveryResultPicker.Pick([
            Report("AA:BB", scanningDeviceId: 5, rssi: -60),
            Report("AA:BB", scanningDeviceId: 9, rssi: -60),
            Report("AA:BB", scanningDeviceId: 3, rssi: -60),
        ]);

        var winner = Assert.Single(result);
        Assert.Equal(9, winner.ScanningDeviceID);
    }

    [Fact]
    public void Pick_NullRssiLosesToAnyRealReading()
    {
        var result = DiscoveryResultPicker.Pick([
            Report("AA:BB", scanningDeviceId: 1, rssi: null),
            Report("AA:BB", scanningDeviceId: 2, rssi: -90),
        ]);

        var winner = Assert.Single(result);
        Assert.Equal(2, winner.ScanningDeviceID);
    }

    [Fact]
    public void Pick_MultipleApMacs_OneWinnerEach()
    {
        var result = DiscoveryResultPicker.Pick([
            Report("AA:BB", scanningDeviceId: 1, rssi: -70),
            Report("AA:BB", scanningDeviceId: 2, rssi: -50),
            Report("CC:DD", scanningDeviceId: 3, rssi: -40),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.DiscoveredApMac == "AA:BB" && r.ScanningDeviceID == 2);
        Assert.Contains(result, r => r.DiscoveredApMac == "CC:DD" && r.ScanningDeviceID == 3);
    }
}
