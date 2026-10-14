using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using CameraControl.AppCore.Presets;
using CameraControl.AppCore.Storage;
using NUnit.Framework;
#if HAS_T7_MEDIA_ADAPTERS
using CameraControl_WinUI.Video;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("VideoAdapters"), NonParallelizable]
public sealed class VideoAdapterTests
{
#if HAS_T7_MEDIA_ADAPTERS
    private string target = null!;

    [SetUp]
    public void Setup() => target = "Versammlungsptzsteuerung/QA/" +
        Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())) + "/RTSP";

    [Test]
    public async Task PngDecoderReadsActualPixelData()
    {
        var decoded = await PngImageDecoder.DecodeAsync(Png());
        Assert.That(decoded.Width, Is.EqualTo(2));
        Assert.That(decoded.Height, Is.EqualTo(3));
        Assert.That(decoded.PixelBytes, Is.EqualTo(24));
    }

    [Test]
    public void PngDecoderRejectsTruncatedData() =>
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await PngImageDecoder.DecodeAsync(Png()[..20]));

    [Test]
    public async Task CompletePngWithBrokenDeflateIsRejected()
    {
        byte[] png = Png();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT")
            {
                png.AsSpan(offset + 8, length).Clear();
                uint crc = Crc(png.AsSpan(offset + 4, length + 4));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset + 8 + length, 4), crc);
            }
            offset += length + 12;
        }
        var directory = Path.Combine(Path.GetTempPath(), "CameraControl-T7-Png-QA", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new JsonSettingsRepository(directory);
            var saved = await repository.SaveAsync(CameraControl.AppCore.AppSettings.CreateDefault(),
                new Dictionary<string, ReadOnlyMemory<byte>> { [CameraControl.AppCore.PresetIds.Home] = png });
            Assert.That(await repository.TryReadImageAsync(saved.Presets[3].ImageFileName), Is.Not.Null,
                "Die Containerprüfung muss den entscheidenden Decoder-Test nicht ersetzen.");
            Assert.ThrowsAsync<PhotoCaptureException>(async () => await PngImageDecoder.DecodeAsync(png));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public void CancelledDecodeDoesNotReturnPixels()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await PngImageDecoder.DecodeAsync(Png(), cancellation.Token));
    }

    [Test]
    public async Task OwnQaCredentialCanBeSavedReadAndDeleted()
    {
        var store = new WindowsRtspCredentialStore(isQa: true, target);
        try
        {
            await store.SaveAsync(target, "local-test-user", "local-test-secret");
            var credential = await store.ReadAsync(target);
            Assert.That(credential is not null && credential.UserName == "local-test-user" && credential.Password == "local-test-secret", Is.True);
            await store.DeleteAsync(target);
            Assert.That(await store.ReadAsync(target), Is.Null);
        }
        finally { await store.DeleteAsync(target); }
    }

    [TestCase("Versammlungsptzsteuerung/RTSP")]
    [TestCase("Versammlungsptzsteuerung/QA/0000000000000000000000000000000000000000000000000000000000000000/RTSP")]
    [TestCase("OtherProduct/RTSP")]
    public void QaCredentialStoreRejectsOtherTargets(string foreignTarget)
    {
        var store = new WindowsRtspCredentialStore(isQa: true, target);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await store.ReadAsync(foreignTarget));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await store.SaveAsync(foreignTarget, "u", "p"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await store.DeleteAsync(foreignTarget));
    }

    [Test]
    public void QaConstructorRejectsProductionTarget() =>
        Assert.Throws<ArgumentException>(() => new WindowsRtspCredentialStore(isQa: true, "Versammlungsptzsteuerung/RTSP"));

    [Test]
    public async Task CancellationPreventsCredentialWrite()
    {
        var store = new WindowsRtspCredentialStore(isQa: true, target);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await store.SaveAsync(target, "u", "p", cancellation.Token));
        Assert.That(await store.ReadAsync(target), Is.Null);
    }

    private static byte[] Png()
    {
        using var bitmap = new Bitmap(2, 3);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 2, Color.Blue);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }
#else
    [TestCase("PNG: echte Pixeldecodierung")]
    [TestCase("PNG: beschädigtes Deflate bei gültiger CRC")]
    [TestCase("Credentials: eigener QA-Speicher und Scopeguard")]
    public void RequiredNativeAdapterIsMissing(string requirement) => Assert.Fail("T7 fehlt: " + requirement);
#endif
}
