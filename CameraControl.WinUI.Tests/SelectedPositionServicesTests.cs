using System.Net;
using System.Net.Sockets;
using CameraControl.AppCore;
using CameraControl.AppCore.Storage;
using CameraControl_WinUI;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("SelectedPositionServices"), NonParallelizable]
public sealed class SelectedPositionServicesTests
{
    private string directory = null!;
    [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "CameraControl.SelectedPosition.Services", Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test]
    public async Task SelectionUsesOneWorkflowStateAndRejectsForeignIdsAndPresets()
    {
        await using var peer = new LocalCamera();
        await using var services = await CreateConnectedAsync(peer);
        services.SelectPreset(PresetIds.Tisch);
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Tisch));
        Assert.That(services.Presets.CurrentDraft!.Settings.Id, Is.EqualTo(PresetIds.Tisch));
        Assert.That(services.SelectedPresetName, Is.EqualTo("Tisch"));
        services.Presets.Select(PresetIds.Redner);
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Redner));
        Assert.Throws<ArgumentException>(() => services.SelectPreset("foreign"));
        var foreign = services.Settings.Presets[0] with { Target = new CameraPosition(9, 9, 9) };
        Assert.ThrowsAsync<ArgumentException>(async () => await services.RecallAsync(foreign));
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Redner));
        Assert.That(await services.RecallAsync(services.Settings.Presets[0]), Is.True);
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Leser), "Recall muss die tatsächliche konfigurierte ID auswählen.");
    }

    [Test]
    public async Task SaveSelectedPositionWritesOnlyTischWithoutRtspOrOtherDrafts()
    {
        await using var peer = new LocalCamera();
        await using var services = await CreateConnectedAsync(peer);
        var before = services.Settings;
        services.Presets.Rename("Leserentwurf bleibt");
        services.SelectPreset(PresetIds.Tisch);
        Assert.That(services.CanSaveSelectedPosition, Is.True);
        await services.SaveSelectedPositionAsync();
        var stored = (await services.Repository.LoadAsync()).Settings;
        Assert.That(stored.Presets[1].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(stored.Presets.Where(value => value.Id != PresetIds.Tisch), Is.EqualTo(before.Presets.Where(value => value.Id != PresetIds.Tisch)));
        Assert.That(services.Settings.Presets, Is.EqualTo(stored.Presets));
        Assert.That(services.Settings.Connection.RtspUrl, Is.Null);
        services.SelectPreset(PresetIds.Leser);
        Assert.That(services.Presets.CurrentDraft!.Settings.Name, Is.EqualTo("Leserentwurf bleibt"));
        Assert.That(services.CanSaveSelectedPositionReason, Is.EqualTo("Details zuerst speichern oder verwerfen."));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveSelectedPositionAsync());
    }

    [Test]
    public async Task SelectionAndSaveRejectHomeMovementDisconnectAndClose()
    {
        await using var peer = new LocalCamera();
        await using var services = await CreateConnectedAsync(peer);
        services.SelectPreset(PresetIds.Home);
        Assert.That(services.CanSaveSelectedPosition, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveSelectedPositionAsync());
        services.SelectPreset(PresetIds.Tisch);
        await services.SetPanTiltAsync(24, 0);
        Assert.That(services.CanSelectPreset, Is.False);
        Assert.That(services.CanSaveSelectedPosition, Is.False);
        Assert.Throws<InvalidOperationException>(() => services.SelectPreset(PresetIds.Redner));
        await services.StopAsync();
        await services.Camera.DisconnectAsync();
        Assert.That(services.CanSelectPreset, Is.True, "Ruhende Offline-Auswahl muss den bisherigen Details-Editor erhalten.");
        services.SelectPreset(PresetIds.Redner);
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Redner));
        Assert.That(services.CanSaveSelectedPosition, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.RecallAsync(services.Settings.Presets[0]));
        await services.CloseAsync();
        Assert.That(services.CanSelectPreset, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveSelectedPositionAsync());
    }

    [Test]
    public async Task ReadBusyRejectsOtherSelectionManualMovementAndDraftCapture()
    {
        await using var peer = new LocalCamera { HoldPan = true };
        await using var services = await CreateConnectedAsync(peer);
        services.SelectPreset(PresetIds.Tisch);
        Task saving = services.SaveSelectedPositionAsync();
        try
        {
            await peer.PanEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(services.IsSavingSelectedPosition, Is.True);
            Assert.That(services.CanSelectPreset, Is.False);
            Assert.Throws<InvalidOperationException>(() => services.SelectPreset(PresetIds.Redner));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SetPanTiltAsync(24, 0));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.CapturePhotoAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SavePresetAsync());
        }
        finally { peer.AllowPan.TrySetResult(); }
        await saving;
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Tisch));
        Assert.That(services.IsSavingSelectedPosition, Is.False);
    }

    [Test]
    public async Task SelectingHomeAfterSuccessfulTischSavePublishesTheDisabledSaveState()
    {
        await using var peer = new LocalCamera();
        await using var services = await CreateConnectedAsync(peer);
        services.SelectPreset(PresetIds.Tisch);
        await services.SaveSelectedPositionAsync();
        string? notifiedId = null;
        bool? notifiedSaveAllowed = null;
        string? notifiedReason = null;
        services.Changed += (_, _) =>
        {
            notifiedId = services.SelectedPresetId;
            notifiedSaveAllowed = services.CanSaveSelectedPosition;
            notifiedReason = services.CanSaveSelectedPositionReason;
        };
        services.SelectPreset(PresetIds.Home);
        Assert.That(services.SelectedPresetId, Is.EqualTo(PresetIds.Home));
        Assert.That(services.CanSaveSelectedPosition, Is.False);
        Assert.That(notifiedId, Is.EqualTo(PresetIds.Home), "Der Auswahlwechsel muss den tatsächlichen Workflow-Zustand publizieren.");
        Assert.That(notifiedSaveAllowed, Is.False);
        Assert.That(notifiedReason, Is.EqualTo("Home hat keinen lokalen Zielwert."));
    }

    private async Task<ApplicationServices> CreateConnectedAsync(LocalCamera peer)
    {
        var defaults = AppSettings.CreateDefault();
        var connection = new ConnectionSettings("127.0.0.1", peer.Port, null, null);
        await new JsonSettingsRepository(directory).SaveAsync(defaults with { Connection = connection,
            Presets = defaults.Presets.Select(value => value.Id == PresetIds.Leser ? value with { Target = new CameraPosition(1, 2, 3) } : value).ToArray() });
        var services = await ApplicationServices.CreateAsync(directory, false, isQa: true);
        await services.ConnectAsync(connection);
        return services;
    }

    private sealed class LocalCamera : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource lifetime = new();
        private readonly Task server;
        public int Port { get; }
        public bool HoldPan { get; init; }
        public TaskCompletionSource PanEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LocalCamera() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; server = ServeAsync(); }
        private async Task ServeAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                using var stream = client.GetStream();
                var packet = new List<byte>();
                byte[] buffer = new byte[1];
                while (await stream.ReadAsync(buffer, lifetime.Token) != 0)
                {
                    packet.Add(buffer[0]);
                    if (buffer[0] != 0xff) continue;
                    byte[] response;
                    if (packet.SequenceEqual(new byte[] { 0x81, 0x09, 0x04, 0x00, 0xff })) response = [0x90, 0x50, 0x02, 0xff];
                    else if (packet.SequenceEqual(new byte[] { 0x81, 0x09, 0x06, 0x12, 0xff }))
                    {
                        if (HoldPan) { PanEntered.TrySetResult(); await AllowPan.Task.WaitAsync(lifetime.Token); }
                        response = [0x90, 0x50, 0x0f, 0x0f, 0x0f, 0x0f, 0, 0, 0, 1, 0xff];
                    }
                    else if (packet.SequenceEqual(new byte[] { 0x81, 0x09, 0x04, 0x47, 0xff })) response = [0x90, 0x50, 1, 2, 3, 4, 0xff];
                    else response = [0x90, 0x41, 0xff, 0x90, 0x51, 0xff];
                    packet.Clear();
                    await stream.WriteAsync(response, lifetime.Token);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { lifetime.Cancel(); listener.Stop(); await server; lifetime.Dispose(); }
    }
}
