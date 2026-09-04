using api.LoRa;
using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>The one piece of the LoRa design testable without real hardware: the
/// SF-&gt;interval curve is pure math, no ChirpStack/gateway involved.</summary>
public class LoRaIntervalTests
{
    [Theory]
    [InlineData(7, 30)]
    [InlineData(9, 120)]
    [InlineData(12, 300)]
    public void ForSpreadingFactor_AnchorPoints_MatchExactly(int sf, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, LoRaInterval.ForSpreadingFactor(sf).TotalSeconds);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(-5)]
    public void ForSpreadingFactor_BelowLowestAnchor_ClampsToSf7(int sf)
    {
        Assert.Equal(30, LoRaInterval.ForSpreadingFactor(sf).TotalSeconds);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    public void ForSpreadingFactor_AboveHighestAnchor_ClampsToSf12(int sf)
    {
        Assert.Equal(300, LoRaInterval.ForSpreadingFactor(sf).TotalSeconds);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    public void ForSpreadingFactor_Interpolated_StrictlyIncreasesWithSf(int sf)
    {
        double below = LoRaInterval.ForSpreadingFactor(sf - 1).TotalSeconds;
        double at = LoRaInterval.ForSpreadingFactor(sf).TotalSeconds;
        double above = LoRaInterval.ForSpreadingFactor(sf + 1).TotalSeconds;
        Assert.True(below < at, $"SF{sf - 1} ({below}s) should be shorter than SF{sf} ({at}s)");
        Assert.True(at < above, $"SF{sf} ({at}s) should be shorter than SF{sf + 1} ({above}s)");
    }

    // EU868's ~1% duty-cycle budget over a 5-minute window is 3s of airtime; a single uplink+
    // downlink at any of these anchor SFs is well under a second of airtime, so even the tightest
    // (SF7, most frequent) retry interval leaves huge margin - this just guards against the curve
    // ever being edited back down near/under the 1% line.
    [Fact]
    public void ForSpreadingFactor_Sf7Interval_LeavesDutyCycleMargin()
    {
        Assert.True(LoRaInterval.ForSpreadingFactor(7).TotalSeconds >= 30);
    }
}
