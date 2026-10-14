using CameraControl.AppCore;
using CameraControl.AppCore.Storage;
using NUnit.Framework;
#if HAS_APPLICATION_SERVICES
using CameraControl_WinUI;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture]
[Category("ApplicationServices")]
public sealed class ApplicationServicesTests
{
#if HAS_APPLICATION_SERVICES
    private string directory = null!;

    [SetUp]
    public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "CameraControl-WinUI-QA", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Test]
    public async Task ReadOnlyPreparationDoesNotCreateSettingsOrConnectCamera()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, readOnly: true);
        Assert.That(services.IsReadOnly, Is.True);
        Assert.That(services.Camera.IsConnected, Is.False);
        Assert.That(File.Exists(Path.Combine(directory, "settings.json")), Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await services.SaveConnectionAsync(new ConnectionSettings(null, 5678, null, null)));
    }

    [Test]
    public async Task CommitAllowsSavingWithoutRequiringAReachableCamera()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, readOnly: true);
        await services.ActivateCommittedAsync();
        await services.SaveConnectionAsync(new ConnectionSettings(null, 5678, null, null));
        Assert.That(services.IsReadOnly, Is.False);
        Assert.That(File.Exists(Path.Combine(directory, "settings.json")), Is.True);
    }

    [Test]
    public async Task ConcurrentConnectionAndWindowUpdatesKeepBothChanges()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, readOnly: false);
        var connection = new ConnectionSettings("127.0.0.1", 15001, null, null);
        var placement = new WindowPlacement(20, 40, 600, 430, 1);
        await Task.WhenAll(services.SaveConnectionAsync(connection), services.SaveMainPlacementAsync(placement));
        var saved = await new JsonSettingsRepository(directory).LoadAsync();
        Assert.That(saved.Settings.Connection, Is.EqualTo(connection));
        Assert.That(saved.Settings.Windows.Main, Is.EqualTo(placement));
        Assert.That(services.Settings.Connection, Is.EqualTo(saved.Settings.Connection));
        Assert.That(services.Settings.Windows, Is.EqualTo(saved.Settings.Windows));
        Assert.That(services.Settings.Presets, Is.EqualTo(saved.Settings.Presets));
    }

    [Test]
    public async Task PresetSaveAndWindowSaveShareTheWholeTransaction()
    {
        await using var services = await ApplicationServices.CreateAsync(directory, readOnly: false);
        services.Presets.Rename("Langer Lesername");
        var placement = new WindowPlacement(50, 50, 640, 480, 1.25);
        await Task.WhenAll(services.SavePresetAsync(), services.SavePreviewPlacementAsync(placement));
        var saved = await new JsonSettingsRepository(directory).LoadAsync();
        Assert.That(saved.Settings.Presets[0].Name, Is.EqualTo("Langer Lesername"));
        Assert.That(saved.Settings.Windows.Preview, Is.EqualTo(placement));
    }
#else
    [Test]
    public void RequiredApplicationServicesAreNotImplementedYet() =>
        Assert.Fail("T5 fehlt: ApplicationServices muss Vorbereitung, Commit und gemeinsame Schreibtransaktionen bereitstellen.");
#endif
}
