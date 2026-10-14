using CameraControl.AppCore;
using CameraControl.AppCore.Presets;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("ConfirmedPhotoServices"), NonParallelizable]
public sealed class ConfirmedPhotoServicesTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private string directory = null!;
    [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "CameraControl.ConfirmedPhoto.Services", Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown()
    {
        string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.ConfirmedPhoto.Services")) + Path.DirectorySeparatorChar;
        Assert.That(Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [TestCase(false), TestCase(true)]
    public async Task NamedPhotoDecisionUpdatesServicesOnlyAfterApproval(bool accepted)
    {
        await using var services = await CreateAsync();
        var original = services.Settings;
        var before = File.ReadAllBytes(Path.Combine(directory, "settings.json"));
        bool saved = await services.SaveSelectedPhotoAsync((photo, token) =>
        {
            Assert.That(photo.Name, Is.EqualTo("Home"));
            Assert.That(photo.PresetId, Is.EqualTo(PresetIds.Home));
            Assert.That(services.IsSavingSelectedPhoto, Is.True);
            Assert.That(services.CanSelectPreset, Is.False);
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "settings.json")), Is.EqualTo(before));
            return Task.FromResult(accepted);
        });
        Assert.That(saved, Is.EqualTo(accepted));
        Assert.That(services.IsSavingSelectedPhoto, Is.False);
        Assert.That(services.CanSelectPreset, Is.True);
        var stored = (await services.Repository.LoadAsync()).Settings;
        Assert.That(services.Settings.Presets, Is.EqualTo(stored.Presets));
        Assert.That(stored.Presets.Where(value => value.Id != PresetIds.Home), Is.EqualTo(original.Presets.Where(value => value.Id != PresetIds.Home)));
        Assert.That(stored.Presets.Single(value => value.Id == PresetIds.Home).Target, Is.Null);
        if (accepted)
            Assert.That(await services.Repository.TryReadImageAsync(stored.Presets.Single(value => value.Id == PresetIds.Home).ImageFileName), Is.EqualTo(Png));
        else Assert.That(File.ReadAllBytes(Path.Combine(directory, "settings.json")), Is.EqualTo(before));
    }

    [Test]
    public async Task OpenPhotoDialogBlocksAnotherSelectionMovementAndConnectionChange()
    {
        await using var services = await CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> saving = services.SaveSelectedPhotoAsync(async (photo, token) =>
        { entered.TrySetResult(); return await answer.Task.WaitAsync(token); });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Throws<InvalidOperationException>(() => services.SelectPreset(PresetIds.Tisch));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SetPanTiltAsync(24, 0));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.CapturePhotoAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SavePresetAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveConnectionAsync(services.Settings.Connection));
        }
        finally { answer.TrySetResult(false); }
        Assert.That(await saving, Is.False);
    }

    [Test]
    public async Task ClosingWithOpenPhotoConfirmationCancelsWithoutWriting()
    {
        await using var services = await CreateAsync();
        byte[] before = File.ReadAllBytes(Path.Combine(directory, "settings.json"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> saving = services.SaveSelectedPhotoAsync(async (photo, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return true; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await services.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.CatchAsync<OperationCanceledException>(async () => await saving);
        Assert.That(File.ReadAllBytes(Path.Combine(directory, "settings.json")), Is.EqualTo(before));
        Assert.That(services.IsClosing, Is.True);
        Assert.That(services.IsSavingSelectedPhoto, Is.False);
    }

    private async Task<CameraControl_WinUI.ApplicationServices> CreateAsync()
    {
        var services = await CameraControl_WinUI.ApplicationServices.CreateAsync(directory, false, true, new Photo());
        await services.SaveConnectionAsync(new ConnectionSettings(null, 5678, "rtsp://127.0.0.1/fixture", null));
        services.SelectPreset(PresetIds.Home);
        return services;
    }

    private sealed class Photo : IPositionPhotoSource
    {
        public Task OpenAndWaitForFrameAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Png); }
    }
}
