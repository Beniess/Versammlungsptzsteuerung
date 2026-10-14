using System.Net;
using System.Net.Sockets;
using CameraControl.AppCore;
using CameraControl.AppCore.Presets;
using NUnit.Framework;
using CameraControl_WinUI;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("ConnectionEditing"), NonParallelizable]
public sealed class ConnectionEditingTests
{
    private string directory = null!;
    [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "CameraControl-Connection-QA", Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test]
    public async Task SavingChangedHostPortDisconnectsTheOldLiveSession()
    {
        await using var server = new LocalVisca();
        await using var services = await ApplicationServices.CreateAsync(directory, false, isQa: true);
        var old = new ConnectionSettings("127.0.0.1", server.Port, null, null);
        await services.SaveConnectionAsync(old);
        await services.ConnectAsync(old);
        Assert.That(services.Camera.IsConnected, Is.True);
        await services.SaveConnectionAsync(old with { ViscaPort = server.Port == 65535 ? 65534 : server.Port + 1 });
        Assert.That(services.Camera.IsConnected, Is.False, "Eine alte Sitzung darf neue gespeicherte Daten nicht als verbunden darstellen.");
        Assert.That(services.IsConnectionChanging, Is.False);
        Assert.That(services.ConnectionRevision, Is.EqualTo(2));
    }

    [Test]
    public async Task ConnectionCannotChangeWhilePhotoUsesTheCurrentSource()
    {
        var photo = new HeldPhoto();
        await using var services = await ApplicationServices.CreateAsync(directory, false, true, photo);
        var old = new ConnectionSettings(null, 5678, "rtsp://127.0.0.1/fixture", null);
        await services.SaveConnectionAsync(old);
        services.Presets.Select(PresetIds.Home);
        var capture = services.CapturePhotoAsync();
        await photo.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveConnectionAsync(old with { RtspUrl = "rtsp://127.0.0.1/other" }));
            Assert.That(services.Settings.Connection, Is.EqualTo(old));
        }
        finally { services.Presets.Cancel(); Assert.CatchAsync<OperationCanceledException>(async () => await capture); }
    }

    [Test]
    public async Task FailedJsonValidationRestoresThePreviousVaultEntry()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, false, true);
        var vault = new MemoryCredentials();
        services.ConfigureCredentialStore(vault);
        await vault.SaveAsync(services.RtspCredentialTarget, "old-user", "old-secret");
        Assert.ThrowsAsync<ArgumentException>(async () => await services.SaveConnectionWithCredentialsAsync(
            new ConnectionSettings(null, 5678, "invalid-url", null), "new-user", "new-secret", false));
        var credential = await vault.ReadAsync(services.RtspCredentialTarget);
        Assert.That(credential, Is.EqualTo(new RtspCredential("old-user", "old-secret")));
        Assert.That(File.Exists(Path.Combine(directory, "settings.json")), Is.False);
        Assert.That(credential!.ToString(), Does.Not.Contain("old-secret"));
    }

    [Test]
    public async Task ReadOnlyCandidateCannotWriteCredentials()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, true, true);
        var vault = new MemoryCredentials();
        services.ConfigureCredentialStore(vault);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveConnectionWithCredentialsAsync(
            new ConnectionSettings(null, 5678, "rtsp://127.0.0.1/fixture", null), "user", "secret", false));
        Assert.That(vault.Writes, Is.Zero);
    }

    private sealed class HeldPhoto : IPositionPhotoSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task OpenAndWaitForFrameAsync(CancellationToken token) { Entered.TrySetResult(); return Task.Delay(Timeout.Infinite, token); }
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class MemoryCredentials : IRtspCredentialStore
    {
        private RtspCredential? value;
        public int Writes { get; private set; }
        public Task SaveAsync(string target, string userName, string password, CancellationToken cancellationToken = default)
        { value = new(userName, password); Writes++; return Task.CompletedTask; }
        public Task<RtspCredential?> ReadAsync(string target, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task DeleteAsync(string target, CancellationToken cancellationToken = default) { value = null; return Task.CompletedTask; }
    }

    private sealed class LocalVisca : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        public int Port { get; }
        public LocalVisca() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; loop = ServeAsync(); }
        private async Task ServeAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                var packet = new List<byte>();
                var buffer = new byte[64];
                while (!stop.IsCancellationRequested)
                {
                    var count = await stream.ReadAsync(buffer, stop.Token);
                    if (count == 0) return;
                    foreach (var value in buffer.Take(count))
                    {
                        packet.Add(value);
                        if (value != 0xff) continue;
                        var power = packet.SequenceEqual(new byte[] { 0x81, 0x09, 0x04, 0x00, 0xff });
                        packet.Clear();
                        await stream.WriteAsync(power ? new byte[] { 0x90, 0x50, 0x02, 0xff } : new byte[] { 0x90, 0x51, 0xff }, stop.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await loop; stop.Dispose(); }
    }
}
