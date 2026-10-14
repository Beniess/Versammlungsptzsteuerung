using System.Buffers.Binary;

namespace CameraControl.AppCore.Storage;

internal static class PngIntegrity
{
    private static ReadOnlySpan<byte> Signature => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

    internal static bool IsComplete(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(Signature)) return false;
        int offset = 8;
        bool header = false;
        bool imageData = false;
        while (offset <= bytes.Length - 12)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (size > int.MaxValue || (long)offset + size + 12 > bytes.Length) return false;
            int length = (int)size;
            ReadOnlySpan<byte> type = bytes.Slice(offset + 4, 4);
            ReadOnlySpan<byte> payload = bytes.Slice(offset + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8 + length, 4));
            if (Crc32(bytes.Slice(offset + 4, length + 4)) != crc) return false;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || offset != 8 || length != 13 || BinaryPrimitives.ReadUInt32BigEndian(payload[..4]) == 0 ||
                    BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) == 0 || payload[10] != 0 || payload[11] != 0 || payload[12] > 1) return false;
                header = true;
            }
            else if (!header) return false;
            else if (type.SequenceEqual("IDAT"u8)) imageData = true;
            else if (type.SequenceEqual("IEND"u8)) return length == 0 && imageData && offset + 12 == bytes.Length;
            offset += length + 12;
        }
        return false;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
        }
        return ~crc;
    }
}
