using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CameraControl.Visca.Tests;

internal static class Fixtures
{
    private static readonly IReadOnlyDictionary<string, byte[]> Packets = File.ReadAllLines(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "packets.tsv"))
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
        .Select(line => line.Split('|'))
        .ToDictionary(parts => parts[0], parts => ParseHex(parts[1]), StringComparer.Ordinal);

    internal static byte[] Wire(string name) => (byte[])Packets[name].Clone();

    internal static ViscaPacket Response(string name)
    {
        byte[] wire = Wire(name);
        if (wire[wire.Length - 1] != 0xff)
            throw new InvalidDataException("Fixture response requires FF.");
        return ViscaPacket.FromBytes(wire, 0, wire.Length - 1);
    }

    internal static byte[] Bytes(ViscaPacket packet) => Enumerable.Range(0, packet.Length)
        .Select(packet.GetByte).ToArray();

    internal static byte[] ParseHex(string value) => value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(part => byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
}
