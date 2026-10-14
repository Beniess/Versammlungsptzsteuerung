using System.Text.Json;
using CameraControl.AppCore.Storage;
using NUnit.Framework;

namespace CameraControl.AppCore.Tests;

[TestFixture, Category("Persistence"), NonParallelizable]
public sealed class PersistenceTests
{
    private string testRoot = null!;
    private string dataDirectory = null!;
    private string SettingsPath => Path.Combine(dataDirectory, "settings.json");
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.AppCore.Tests", Guid.NewGuid().ToString("N")));
        dataDirectory = Path.Combine(testRoot, "Data");
        Directory.CreateDirectory(testRoot);
    }

    [TearDown]
    public void CleanUp()
    {
        string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.AppCore.Tests")) + Path.DirectorySeparatorChar;
        Assert.That(testRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(testRoot, recursive: true);
        Assert.That(Directory.Exists(testRoot), Is.False);
        TestContext.WriteLine("Removed isolated data directory: " + testRoot);
    }

    private static AppSettings Configured(string name = "Lesepult")
    {
        AppSettings defaults = AppSettings.CreateDefault();
        return defaults with
        {
            Connection = new ConnectionSettings("127.0.0.1", 52381, "rtsp://127.0.0.1:8554/local", null),
            Presets = defaults.Presets.Select(preset => preset.Id == PresetIds.Leser
                ? preset with { Name = name, Target = new CameraPosition(1, -1, 4660) } : preset).ToArray(),
            Windows = new WindowSettings(new WindowPlacement(-1200, 15, 600, 430, 1.25), new WindowPlacement(10, 20, 800, 450, 1))
        };
    }

    private static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Image(string id = PresetIds.Leser) =>
        new Dictionary<string, ReadOnlyMemory<byte>> { [id] = Png };

    [Test]
    public async Task MissingFileStartsUnconfiguredWithoutCreatingFiles()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        SettingsLoadResult loaded = await repository.LoadAsync();
        Assert.That(loaded.Status, Is.EqualTo(SettingsLoadStatus.Missing));
        Assert.That(loaded.Settings.SchemaVersion, Is.EqualTo(1));
        Assert.That(loaded.Settings.Connection.Host, Is.Null);
        Assert.That(loaded.Settings.Connection.RtspUrl, Is.Null);
        Assert.That(loaded.Settings.Connection.IsConfigured, Is.False);
        Assert.That(loaded.Settings.Presets.Select(p => p.Id), Is.EqualTo(new[] { "leser", "tisch", "redner", "home" }));
        Assert.That(loaded.Settings.Presets.Take(3).All(p => p.Target is null && !p.IsConfigured), Is.True);
        Assert.That(loaded.Settings.Presets.All(p => p.ImageFileName is null), Is.True);
        Assert.That(Directory.Exists(dataDirectory), Is.False);
    }

    [Test]
    public async Task ValuesAndImageSurviveNewRepositoryInstancesAndVersionDirectories()
    {
        Directory.CreateDirectory(Path.Combine(testRoot, "Versions", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(testRoot, "Versions", "2.0.0"));
        var versionA = new JsonSettingsRepository(dataDirectory);
        AppSettings saved = await versionA.SaveAsync(Configured(), Image());
        var versionB = new JsonSettingsRepository(dataDirectory);
        SettingsLoadResult loaded = await versionB.LoadAsync();
        Assert.That(loaded.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
        Assert.That(loaded.Settings.Connection, Is.EqualTo(saved.Connection));
        Assert.That(loaded.Settings.Presets, Is.EqualTo(saved.Presets));
        Assert.That(loaded.Settings.Windows, Is.EqualTo(saved.Windows));
        Assert.That(await versionB.TryReadImageAsync(loaded.Settings.Presets[0].ImageFileName), Is.EqualTo(Png));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(document.RootElement.GetProperty("connection").GetProperty("viscaPort").GetInt32(), Is.EqualTo(52381));
        Assert.That(document.RootElement.GetProperty("presets")[0].GetProperty("target").GetProperty("zoom").GetInt32(), Is.EqualTo(4660));
        Assert.That(Directory.GetFiles(testRoot, "settings.json", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        TestContext.WriteLine("Persisted JSON: " + document.RootElement.GetRawText());
        TestContext.WriteLine("Persisted PNG bytes: " + Png.Length);
    }

    [TestCase("{", SettingsLoadStatus.Invalid)]
    [TestCase("null", SettingsLoadStatus.Invalid)]
    [TestCase("{}", SettingsLoadStatus.Invalid)]
    [TestCase("{\"schemaVersion\":2}", SettingsLoadStatus.UnsupportedVersion)]
    [TestCase("{\"schemaVersion\":\"1\"}", SettingsLoadStatus.Invalid)]
    public async Task InvalidDataCannotBeSilentlyOverwritten(string json, SettingsLoadStatus expected)
    {
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(SettingsPath, json);
        var repository = new JsonSettingsRepository(dataDirectory);
        SettingsLoadResult loaded = await repository.LoadAsync();
        Assert.That(loaded.Status, Is.EqualTo(expected));
        Assert.That(loaded.Settings.Connection.IsConfigured, Is.False);
        Assert.That(loaded.Message, Is.Not.Empty);
        Assert.ThrowsAsync<SettingsStorageException>(async () => await repository.SaveAsync(Configured()));
        Assert.That(await File.ReadAllTextAsync(SettingsPath), Is.EqualTo(json));
        AppSettings reset = await repository.ResetAsync();
        Assert.That(reset.Connection.IsConfigured, Is.False);
        Assert.That((await repository.LoadAsync()).Status, Is.EqualTo(SettingsLoadStatus.Loaded));
    }

    [TestCase(0), TestCase(65536)]
    public void InvalidPortIsRejectedBeforeWriting(int port)
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured();
        settings = settings with { Connection = settings.Connection with { ViscaPort = port } };
        Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(settings));
        Assert.That(File.Exists(SettingsPath), Is.False);
    }

    [TestCase("rtsp://user:secret@127.0.0.1/live")]
    [TestCase("https://127.0.0.1/live")]
    [TestCase("not-a-url")]
    public void InvalidOrCredentialBearingRtspUrlIsRejected(string url)
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured();
        settings = settings with { Connection = settings.Connection with { RtspUrl = url } };
        var exception = Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(settings));
        Assert.That(exception!.Message, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task OnlyCredentialReferenceCanBePersisted()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured();
        settings = settings with { Connection = settings.Connection with { CredentialTarget = "Versammlungsptzsteuerung/Camera" } };
        await repository.SaveAsync(settings);
        string json = await File.ReadAllTextAsync(SettingsPath);
        Assert.That(json, Does.Contain("Versammlungsptzsteuerung/Camera"));
        Assert.That(json, Does.Not.Contain("password").IgnoreCase.And.Not.Contain("username").IgnoreCase);
    }

    [Test]
    public void NegativeZoomAndHomeTargetAreRejected()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        foreach (AppSettings invalid in new[]
        {
            Configured() with { Presets = Configured().Presets.Select(p => p.Id == PresetIds.Leser ? p with { Target = new CameraPosition(0, 0, -1) } : p).ToArray() },
            Configured() with { Presets = Configured().Presets.Select(p => p.Id == PresetIds.Home ? p with { Target = new CameraPosition(0, 0, 0) } : p).ToArray() }
        }) Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(invalid));
    }

    [Test]
    public async Task SignedPositionLimitsAndExplicitZeroRemainValid()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured() with { Presets = Configured().Presets.Select(p => p.Id switch
        {
            PresetIds.Leser => p with { Target = new CameraPosition(short.MinValue, short.MaxValue, short.MaxValue) },
            PresetIds.Tisch => p with { Target = new CameraPosition(0, 0, 0) },
            _ => p
        }).ToArray() };
        await repository.SaveAsync(settings);
        AppSettings loaded = (await repository.LoadAsync()).Settings;
        Assert.That(loaded.Presets[0].Target, Is.EqualTo(new CameraPosition(short.MinValue, short.MaxValue, short.MaxValue)));
        Assert.That(loaded.Presets[1].IsConfigured, Is.True);
        Assert.That(loaded.Presets[2].IsConfigured, Is.False);
    }

    [TestCase("../outside.png")]
    [TestCase("C:\\outside.png")]
    [TestCase("subfolder/image.png")]
    [TestCase("image.jpg")]
    public void ExternalOrForeignImageReferencesAreRejected(string fileName)
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured() with { Presets = Configured().Presets.Select(p => p.Id == PresetIds.Leser
            ? p with { ImageFileName = fileName } : p).ToArray() };
        Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(settings));
    }

    [Test]
    public void PresetIdsOrderAndHomeNameAreFixed()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        foreach (AppSettings settings in new[]
        {
            Configured() with { Presets = Configured().Presets.Reverse().ToArray() },
            Configured() with { Presets = Configured().Presets.Take(3).ToArray() },
            Configured() with { Presets = Configured().Presets.Select(p => p.Id == PresetIds.Home ? p with { Name = "Anders" } : p).ToArray() }
        }) Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(settings));
    }

    [Test]
    public void InvalidWindowPlacementIsRejected()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings settings = Configured() with { Windows = new WindowSettings(new WindowPlacement(0, 0, 0, 430, double.NaN), null) };
        Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(settings));
    }

    [Test]
    public async Task MissingAndCorruptImageDoNotBlockValidSettings()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings saved = await repository.SaveAsync(Configured(), Image());
        string imageName = saved.Presets[0].ImageFileName!;
        await File.WriteAllBytesAsync(Path.Combine(dataDirectory, "Images", imageName), new byte[] { 1, 2, 3 });
        Assert.That((await repository.LoadAsync()).Status, Is.EqualTo(SettingsLoadStatus.Loaded));
        Assert.That(await repository.TryReadImageAsync(imageName), Is.Null);
        File.Delete(Path.Combine(dataDirectory, "Images", imageName));
        Assert.That(await repository.TryReadImageAsync(imageName), Is.Null);
        Assert.That((await repository.LoadAsync()).Settings.Presets[0].Target, Is.EqualTo(new CameraPosition(1, -1, 4660)));
    }

    [Test]
    public async Task ImageReplacementCommitsNewReferenceBeforeRemovingOldImage()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings first = await repository.SaveAsync(Configured(), Image());
        string oldImage = Path.Combine(dataDirectory, "Images", first.Presets[0].ImageFileName!);
        AppSettings second = await repository.SaveAsync(first, Image());
        Assert.That(second.Presets[0].ImageFileName, Is.Not.EqualTo(first.Presets[0].ImageFileName));
        Assert.That(await repository.TryReadImageAsync(second.Presets[0].ImageFileName), Is.EqualTo(Png));
        Assert.That(File.Exists(oldImage), Is.False);
        Assert.That(Directory.GetFiles(dataDirectory, "*.bak", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.GetFiles(dataDirectory, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task JsonReplacementFailurePreservesExistingJsonAndImage()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings first = await repository.SaveAsync(Configured(), Image());
        byte[] previousJson = await File.ReadAllBytesAsync(SettingsPath);
        string oldImage = Path.Combine(dataDirectory, "Images", first.Presets[0].ImageFileName!);
        using (var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAsync<SettingsStorageException>(async () => await repository.SaveAsync(first with
            { Connection = first.Connection with { ViscaPort = 12345 } }, Image()));
        }
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(previousJson));
        Assert.That(await File.ReadAllBytesAsync(oldImage), Is.EqualTo(Png));
        Assert.That(Directory.GetFiles(Path.Combine(dataDirectory, "Images")), Has.Length.EqualTo(1));
        Assert.That(Directory.GetFiles(dataDirectory, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task ImageWriteFailurePreservesExistingSettings()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        await repository.SaveAsync(Configured());
        byte[] previous = await File.ReadAllBytesAsync(SettingsPath);
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "Images"), "blocked");
        Assert.ThrowsAsync<SettingsStorageException>(async () => await repository.SaveAsync(Configured(), Image()));
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(previous));
    }

    [Test]
    public async Task InvalidPngDoesNotChangeSavedData()
    {
        var repository = new JsonSettingsRepository(dataDirectory);
        await repository.SaveAsync(Configured());
        byte[] previous = await File.ReadAllBytesAsync(SettingsPath);
        Assert.ThrowsAsync<ArgumentException>(async () => await repository.SaveAsync(Configured(),
            new Dictionary<string, ReadOnlyMemory<byte>> { [PresetIds.Leser] = new byte[] { 1, 2, 3 } }));
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(previous));
    }

    [Test]
    public async Task HomePhotoHasNoPositionAndOldConfigIsIgnored()
    {
        await File.WriteAllTextAsync(Path.Combine(testRoot, "App.config"), "invalid old settings, never read");
        var repository = new JsonSettingsRepository(dataDirectory);
        AppSettings saved = await repository.SaveAsync(AppSettings.CreateDefault(), Image(PresetIds.Home));
        Assert.That(saved.Presets[3].Target, Is.Null);
        Assert.That(await repository.TryReadImageAsync(saved.Presets[3].ImageFileName), Is.EqualTo(Png));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(testRoot, "App.config")), Is.EqualTo("invalid old settings, never read"));
    }

    [Test]
    public void WindowsDataPathDoesNotDependOnProgramVersion()
    {
        Assert.That(WindowsDataPath.GetDirectory(), Is.EqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Versammlungsptzsteuerung", "Data")));
    }

    [Test]
    public async Task DifferentProgramWorkingDirectoriesUseSameDataDirectory()
    {
        string versionA = Path.Combine(testRoot, "Versions", "1.0.0");
        string versionB = Path.Combine(testRoot, "Versions", "2.0.0");
        Directory.CreateDirectory(versionA);
        Directory.CreateDirectory(versionB);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = versionA;
            await new JsonSettingsRepository(dataDirectory).SaveAsync(Configured());
            Environment.CurrentDirectory = versionB;
            SettingsLoadResult loaded = await new JsonSettingsRepository(dataDirectory).LoadAsync();
            Assert.That(loaded.Status, Is.EqualTo(SettingsLoadStatus.Loaded));
            Assert.That(loaded.Settings.Presets[0].Target, Is.EqualTo(new CameraPosition(1, -1, 4660)));
            Assert.That(Directory.GetFiles(versionA), Is.Empty);
            Assert.That(Directory.GetFiles(versionB), Is.Empty);
            TestContext.WriteLine("Version directories: " + versionA + " -> " + versionB + "; common data: " + dataDirectory);
        }
        finally { Environment.CurrentDirectory = originalDirectory; }
    }

    [Test]
    public void CancelledSaveDoesNotCreateSettings()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var repository = new JsonSettingsRepository(dataDirectory);
        Assert.That(async () => await repository.SaveAsync(Configured(), cancellationToken: cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.Exists(SettingsPath), Is.False);
    }

    [Test]
    public async Task TwoRepositoryInstancesCannotRemoveAnUncommittedImage()
    {
        var firstStaged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRepository = new JsonSettingsRepository(dataDirectory, async () =>
        {
            firstStaged.SetResult(true);
            await releaseFirst.Task;
        });
        var secondRepository = new JsonSettingsRepository(dataDirectory + Path.DirectorySeparatorChar);
        Task<AppSettings> firstSave = firstRepository.SaveAsync(Configured("Erster Name"), Image());
        Task<AppSettings>? secondSave = null;
        bool secondCommittedBeforeFirst = false;
        try
        {
            Assert.That(await Task.WhenAny(firstStaged.Task, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(firstStaged.Task));
            secondSave = secondRepository.SaveAsync(Configured("Zweiter Name"), Image());
            secondCommittedBeforeFirst = await Task.WhenAny(secondSave, Task.Delay(150)) == secondSave;
        }
        finally { releaseFirst.TrySetResult(true); }
        await firstSave;
        if (secondSave is not null) await secondSave;
        AppSettings loaded = (await secondRepository.LoadAsync()).Settings;
        byte[]? savedImage = await secondRepository.TryReadImageAsync(loaded.Presets[0].ImageFileName);
        TestContext.WriteLine("Second committed while first image was uncommitted: " + secondCommittedBeforeFirst);
        TestContext.WriteLine("Final referenced image exists and has PNG data: " + (savedImage is not null));
        Assert.That(secondCommittedBeforeFirst, Is.False);
        Assert.That(savedImage, Is.EqualTo(Png));
    }
}
