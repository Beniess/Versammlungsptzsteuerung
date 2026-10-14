using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CameraControl.AppCore;
using CameraControl.AppCore.Control;
using CameraControl.AppCore.Presets;
using NUnit.Framework;
#if HAS_APPLICATION_SERVICES
using CameraControl_WinUI;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("ServicesClose"), NonParallelizable]
public sealed class ServicesCloseTests
{
#if HAS_APPLICATION_SERVICES
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "CameraControl-WinUI-Close-QA", Guid.NewGuid().ToString("N"));

    [Test]
    public async Task DuplicateCloseCallsWaitForTheSameCleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var services = await ApplicationServices.CreateAsync(NewDirectory(), readOnly: true);
        services.ConfigurePreview(new NoPhoto(), _ => Task.CompletedTask, async () => { entered.SetResult(); await release.Task; });
        var first = services.CloseAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = services.CloseAsync();
        try
        {
            Assert.That(second, Is.SameAs(first), "Ein zweiter Aufruf muss dieselbe noch laufende Schließaufgabe zurückgeben.");
            Assert.That(second.IsCompleted, Is.False);
        }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); }
    }

    [Test]
    public async Task FailedStopStillDisposesPreviewAndCamera()
    {
        await using var server = new LoopbackServer { RejectStop = true };
        await using var services = await ApplicationServices.CreateAsync(NewDirectory(), readOnly: false);
        var previewDisposed = false;
        services.ConfigurePreview(new NoPhoto(), _ => Task.CompletedTask, () => { previewDisposed = true; return ValueTask.CompletedTask; });
        await services.ConnectAsync(new ConnectionSettings("127.0.0.1", server.Port, null, null));
        await services.Camera.SetPanTiltAsync(12, 0);
        try { await services.CloseAsync(); }
        catch (CameraOperationException) { }
        Assert.That(previewDisposed, Is.True, "Ein fehlgeschlagener Stopp darf die Ressourcenfreigabe nicht auslassen.");
        Assert.That(services.Camera.IsConnected, Is.False);
        Assert.That(services.LastShutdownError, Is.Not.Null.And.Not.Empty, "Ein fehlgeschlagener Stopp darf nicht als bestätigt gelten.");
    }

    [Test]
    public async Task CloseCancelsAndAwaitsAnOwnedRecallBeforeCameraDispose()
    {
        await using var server = new LoopbackServer { HoldHome = true };
        await using var services = await ApplicationServices.CreateAsync(NewDirectory(), readOnly: false);
        await services.ConnectAsync(new ConnectionSettings("127.0.0.1", server.Port, null, null));
        var method = typeof(ApplicationServices).GetMethod("RecallAsync", [typeof(PresetSettings), typeof(CancellationToken)]);
        Assert.That(method, Is.Not.Null, "Der UI-Auftrag muss über den Services-Besitzer geführt werden.");
        var recall = (Task<bool>)method!.Invoke(services, [new PresetSettings(PresetIds.Home, "Home", null, null), CancellationToken.None])!;
        await server.HomeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await services.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(recall.IsCompleted, Is.True);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await recall);
        Assert.That(services.Camera.IsConnected, Is.False);
    }

    [TestCase("timeout")]
    [TestCase("decode")]
    [TestCase("io")]
    public async Task AFailedOwnedPhotoAdapterTaskStillReleasesResources(string failure)
    {
        var source = new HeldFailingPhoto(failure);
        var directory = NewDirectory();
        await using var services = await ApplicationServices.CreateAsync(directory, readOnly: false, isQa: true, source);
        var disposed = false;
        services.ConfigurePreview(source, _ => Task.CompletedTask, () => { disposed = true; return ValueTask.CompletedTask; });
        services.Presets.Select(PresetIds.Home);
        var capture = services.CapturePhotoAsync();
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var close = services.CloseAsync();
        await source.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.That(close.IsCompleted, Is.False, "Schließen muss den gehaltenen eigenen Fotoauftrag abwarten.");
            Assert.That(disposed, Is.False);
        }
        finally { source.Release.TrySetResult(); }
        await close.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(capture.IsCompleted, Is.True);
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await capture);
        Assert.That(disposed, Is.True, "Ein Adapterfehler muss vor der Ressourcenfreigabe abgefangen werden.");
        Assert.That(services.Camera.IsConnected, Is.False);
        Assert.That(services.LastShutdownError, Is.Not.Empty);
    }

    private sealed class HeldFailingPhoto(string failure) : IPositionPhotoSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Entered.TrySetResult();
            await Release.Task;
            throw failure switch
            {
                "timeout" => new TimeoutException("Der sichtbare Player liefert kein Bild."),
                "decode" => new COMException("Die PNG-Decodierung ist fehlgeschlagen."),
                _ => new IOException("Das Foto kann nicht gelesen werden.")
            };
        }
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoPhoto : IPositionPhotoSource
    {
        public Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        public bool RejectStop { get; init; }
        public bool HoldHome { get; init; }
        public int Port { get; }
        public TaskCompletionSource HomeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LoopbackServer()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            loop = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                var packet = new List<byte>();
                var buffer = new byte[32];
                while (!stop.IsCancellationRequested)
                {
                    var count = await stream.ReadAsync(buffer, stop.Token);
                    if (count == 0) break;
                    for (var index = 0; index < count; index++)
                    {
                        packet.Add(buffer[index]);
                        if (buffer[index] != 0xff) continue;
                        var bytes = packet.ToArray();
                        packet.Clear();
                        if (bytes.SequenceEqual(new byte[] { 0x81, 0x01, 0x06, 0x04, 0xff }) && HoldHome)
                        {
                            HomeEntered.TrySetResult();
                            await Task.Delay(Timeout.Infinite, stop.Token);
                        }
                        var isPower = bytes.SequenceEqual(new byte[] { 0x81, 0x09, 0x04, 0x00, 0xff });
                        var isStop = bytes.Length == 9 && bytes[2] == 0x06 && bytes[6] == 3 && bytes[7] == 3;
                        var response = isPower ? new byte[] { 0x90, 0x50, 0x02, 0xff } :
                            isStop && RejectStop ? new byte[] { 0x90, 0x60, 0x02, 0xff } : new byte[] { 0x90, 0x51, 0xff };
                        await stream.WriteAsync(response, stop.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            await loop;
            stop.Dispose();
        }
    }
#endif
}
