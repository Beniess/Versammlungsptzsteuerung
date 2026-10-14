using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Buffers.Binary;
using System.IO.Compression;
using CameraControl.AppCore.Presets;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CameraControl_WinUI.Video;

public readonly record struct DecodedPng(uint Width, uint Height, int PixelBytes);

public static class PngImageDecoder
{
    public static async Task<DecodedPng> DecodeAsync(byte[] png, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) throw new InvalidDataException();
            ulong expectedBytes = (ulong)decoder.PixelWidth * decoder.PixelHeight * 4;
            if (expectedBytes > int.MaxValue) throw new InvalidDataException();
            ValidateCompression(png, cancellationToken);
            var provider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken).ConfigureAwait(false);
            byte[] pixels = provider.DetachPixelData();
            if ((ulong)pixels.LongLength != expectedBytes) throw new InvalidDataException();
            cancellationToken.ThrowIfCancellationRequested();
            return new DecodedPng(decoder.PixelWidth, decoder.PixelHeight, pixels.Length);
        }
        catch (Exception error) when (error is COMException or IOException or InvalidDataException or ArgumentException)
        {
            throw new PhotoCaptureException("Die Aufnahme konnte nicht vollständig als PNG-Bild decodiert werden.");
        }
    }

    private static void ValidateCompression(byte[] png, CancellationToken cancellationToken)
    {
        using var compressed = new MemoryStream();
        int offset = 8;
        while (offset <= png.Length - 12)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (size > int.MaxValue || (long)offset + size + 12 > png.Length) throw new InvalidDataException();
            int length = (int)size;
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] buffer = new byte[8192];
        long decodedBytes = 0;
        int read;
        while ((read = inflater.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decodedBytes += read;
        }
        if (decodedBytes == 0) throw new InvalidDataException();
        if (png[28] == 0)
        {
            int channels = png[25] switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException() };
            uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
            uint height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
            long rowBytes = ((long)width * channels * png[24] + 7) / 8;
            if (decodedBytes != (rowBytes + 1) * height) throw new InvalidDataException();
        }
    }
}
