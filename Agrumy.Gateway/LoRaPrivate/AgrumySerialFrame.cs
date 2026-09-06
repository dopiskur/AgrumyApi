namespace api.Gateway.LoRaPrivate
{
    /// Direction byte of a serial frame between Agrumy.Gateway and its locally-attached ESP32+SX126x radio-frontend board - see AgrumySerialFrame remarks for the full wire layout.
    public enum AgrumySerialFrameType : byte
    {
        Uplink = 0x01,
        Downlink = 0x02,
    }

    /// One frame of the serial protocol between Agrumy.Gateway and its radio-frontend board (a small
    /// ESP32+SX126x sketch running RadioLib in raw PHY mode, not LoRaWAN - see AgrumyFirmware's
    /// LoRaPrivateController). Wire layout, all multi-byte fields big-endian:
    /// [0]=0xA5 marker, [1]=AgrumySerialFrameType, [2-3]=nodeAddress (source for Uplink, destination
    /// for Downlink), [4]=RSSI as signed byte (Uplink only, 0 for Downlink), [5-6]=payload length,
    /// [7..]=payload bytes (the same {"t":...} JSON envelope AgrumyFirmware's LoRaPayloadLogic
    /// produces/ChirpStackUplinkService already parses), last byte=XOR checksum of every byte before it.
    /// Deliberately no ACK/retry at this layer - the LoRa PHY's own CRC (RadioLib setCRC) already
    /// rejects corrupt over-the-air packets before they ever reach the serial link.
    public static class AgrumySerialFrame
    {
        private const byte Marker = 0xA5;
        private const int HeaderLength = 7; // marker + type + address(2) + rssi + length(2)

        public static byte[] EncodeDownlink(ushort destAddress, ReadOnlySpan<byte> payload)
        {
            var frame = new byte[HeaderLength + payload.Length + 1];
            frame[0] = Marker;
            frame[1] = (byte)AgrumySerialFrameType.Downlink;
            frame[2] = (byte)(destAddress >> 8);
            frame[3] = (byte)destAddress;
            frame[4] = 0; // RSSI not meaningful for a downlink
            frame[5] = (byte)(payload.Length >> 8);
            frame[6] = (byte)payload.Length;
            payload.CopyTo(frame.AsSpan(HeaderLength));
            frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
            return frame;
        }

        /// One decoded uplink frame - null fields on failure describe why (bad marker/checksum, or payload not fully received yet).
        public readonly record struct DecodedUplink(ushort SourceAddress, sbyte Rssi, byte[] Payload);

        /// Attempts to decode one uplink frame starting at the front of `buffer`. Returns the number of
        /// bytes consumed (0 if not enough data yet, or 1 to resync past a bad marker byte-by-byte)
        /// and the decoded frame via `result` when a full, checksum-valid frame was found.
        public static int TryDecodeUplink(ReadOnlySpan<byte> buffer, out DecodedUplink? result)
        {
            result = null;
            if (buffer.Length == 0)
            {
                return 0;
            }
            if (buffer[0] != Marker)
            {
                return 1; // resync: drop one byte and let the caller retry
            }
            if (buffer.Length < HeaderLength)
            {
                return 0; // wait for more bytes
            }
            if (buffer[1] != (byte)AgrumySerialFrameType.Uplink)
            {
                return 1;
            }
            int payloadLength = (buffer[5] << 8) | buffer[6];
            int frameLength = HeaderLength + payloadLength + 1;
            if (buffer.Length < frameLength)
            {
                return 0;
            }
            if (Checksum(buffer[..(frameLength - 1)]) != buffer[frameLength - 1])
            {
                return 1; // resync past the bad marker byte, not the whole (possibly mis-sized) frame
            }

            ushort sourceAddress = (ushort)((buffer[2] << 8) | buffer[3]);
            sbyte rssi = (sbyte)buffer[4];
            byte[] payload = buffer.Slice(HeaderLength, payloadLength).ToArray();
            result = new DecodedUplink(sourceAddress, rssi, payload);
            return frameLength;
        }

        private static byte Checksum(ReadOnlySpan<byte> data)
        {
            byte x = 0;
            foreach (byte b in data)
            {
                x ^= b;
            }
            return x;
        }
    }
}
