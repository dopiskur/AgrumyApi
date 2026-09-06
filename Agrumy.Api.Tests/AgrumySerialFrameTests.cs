using System.Text;
using api.Gateway.LoRaPrivate;

namespace Agrumy.Api.Tests;

public class AgrumySerialFrameTests
{
    [Fact]
    public void EncodeDownlink_ThenDecodedAsUplinkShape_RoundTripsHeaderFields()
    {
        // Downlink and uplink share the exact same wire layout except the type byte and the
        // meaning of the RSSI byte - encoding a downlink and flipping the type byte to Uplink
        // exercises the same TryDecodeUplink path without needing two near-identical codecs.
        byte[] payload = Encoding.UTF8.GetBytes("{\"ok\":true}");
        byte[] frame = AgrumySerialFrame.EncodeDownlink(42, payload);
        frame[1] = 0x01; // AgrumySerialFrameType.Uplink
        RecomputeChecksum(frame);

        int consumed = AgrumySerialFrame.TryDecodeUplink(frame, out var decoded);

        Assert.Equal(frame.Length, consumed);
        Assert.NotNull(decoded);
        Assert.Equal((ushort)42, decoded!.Value.SourceAddress);
        Assert.Equal(payload, decoded.Value.Payload);
    }

    [Fact]
    public void TryDecodeUplink_IncompleteFrame_ReturnsZeroAndWaitsForMoreBytes()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"t\":\"sensor\"}");
        byte[] frame = AgrumySerialFrame.EncodeDownlink(1, payload);
        frame[1] = 0x01;
        RecomputeChecksum(frame);

        int consumed = AgrumySerialFrame.TryDecodeUplink(frame.AsSpan(0, frame.Length - 3), out var decoded);

        Assert.Equal(0, consumed);
        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeUplink_CorruptChecksum_ResyncsOneByteAtATime()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"t\":\"event\"}");
        byte[] frame = AgrumySerialFrame.EncodeDownlink(7, payload);
        frame[1] = 0x01;
        RecomputeChecksum(frame);
        frame[^1] ^= 0xFF; // corrupt the checksum byte

        int consumed = AgrumySerialFrame.TryDecodeUplink(frame, out var decoded);

        Assert.Equal(1, consumed);
        Assert.Null(decoded);
    }

    [Fact]
    public void TryDecodeUplink_GarbageBeforeMarker_ResyncsByteByByte()
    {
        byte[] garbage = [0x00, 0x11, 0x22];

        int consumed = AgrumySerialFrame.TryDecodeUplink(garbage, out var decoded);

        Assert.Equal(1, consumed);
        Assert.Null(decoded);
    }

    private static void RecomputeChecksum(byte[] frame)
    {
        byte x = 0;
        for (int i = 0; i < frame.Length - 1; i++)
        {
            x ^= frame[i];
        }
        frame[^1] = x;
    }
}
