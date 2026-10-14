using CameraControl.AppCore.Control;
using CameraControl.AppCore.Presets;
using CameraControl.AppCore.Storage;
using CameraControl.Visca;
using CameraControl.Visca.Tests;
using NUnit.Framework;

namespace CameraControl.AppCore.Tests;

[TestFixture, Category("Presets"), NonParallelizable]
public sealed class PresetWorkflowTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private string testRoot = null!;
    private string dataDirectory = null!;
    private JsonSettingsRepository repository = null!;
    private CameraSession? camera;
    private PresetWorkflow? workflow;
    private ControlRecordingClient client = null!;
    private TestPhotoSource photo = null!;
    private AppSettings initial = null!;
    private string SettingsPath => Path.Combine(dataDirectory, "settings.json");
    private string OriginalImagePath => Path.Combine(dataDirectory, "Images", initial.Presets[0].ImageFileName!);
    private PresetDraft Draft => workflow!.CurrentDraft!;
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.PresetWorkflow.Tests", Guid.NewGuid().ToString("N")));
        dataDirectory = Path.Combine(testRoot, "Data");
        repository = new JsonSettingsRepository(dataDirectory);
        Directory.CreateDirectory(testRoot);
    }

    [TearDown]
    public async Task Cleanup()
    {
        if (workflow is not null) await workflow.DisposeAsync();
        if (camera is not null) await camera.DisposeAsync();
        string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.PresetWorkflow.Tests")) + Path.DirectorySeparatorChar;
        Assert.That(testRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(testRoot, recursive: true);
        Assert.That(Directory.Exists(testRoot), Is.False);
        TestContext.WriteLine("Removed isolated preset directory: " + testRoot);
        workflow = null;
        camera = null;
    }

    private async Task StartAsync(string selectedId = PresetIds.Leser, bool samePosition = false)
    {
        client = new ControlRecordingClient();
        camera = new CameraSession(_ => client.Controller());
        var defaults = AppSettings.CreateDefault();
        AppSettings configured = defaults with
        {
            Connection = new ConnectionSettings("127.0.0.1", 52381, null, null),
            Presets = defaults.Presets.Select(preset => preset.Id == PresetIds.Leser ? preset with
            { Target = samePosition ? new CameraPosition(-1, 1, 4660) : new CameraPosition(1, -1, 4660) } : preset).ToArray()
        };
        initial = await repository.SaveAsync(configured, new Dictionary<string, ReadOnlyMemory<byte>> { [PresetIds.Leser] = Png });
        photo = new TestPhotoSource();
        await camera.ConnectAsync(initial.Connection);
        client.Clear();
        workflow = new PresetWorkflow(repository, camera, photo, initial);
        workflow.Select(selectedId);
    }

    [Test]
    public async Task NormalPhotoChangesOnlyDraftUntilSave()
    {
        await StartAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        await workflow!.CapturePhotoAsync();
        Assert.That(photo.Calls, Is.EqualTo(new[] { "visible-frame", "validated-png" }));
        Assert.That(Draft.Settings.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(Draft.HasNewPhoto, Is.True);
        Assert.That(Draft.IsDirty, Is.True);
        Assert.That(await workflow.GetDraftImageAsync(), Is.EqualTo(Png));
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(workflow.ActiveSettings.Presets, Is.EqualTo(initial.Presets));
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-query"), Fixtures.Wire("zoom-query"), Fixtures.Wire("pan-query"), Fixtures.Wire("zoom-query") }));
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoSavesOnlyTheNamedSelectionAfterApproval()
    {
        await StartAsync();
        workflow!.Rename("Leser bleibt ein Entwurf");
        workflow.Select(PresetIds.Tisch);
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft originalDraft = Draft;
        bool asked = false;
        bool saved = await workflow.CaptureAndSavePhotoAsync(async (confirmation, token) =>
        {
            asked = true;
            Assert.That(confirmation.PresetId, Is.EqualTo(PresetIds.Tisch));
            Assert.That(confirmation.Name, Is.EqualTo("Tisch"));
            Assert.That(confirmation.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
            Assert.That(confirmation.Png.ToArray(), Is.EqualTo(Png));
            Assert.That(workflow.IsBusy, Is.True);
            Assert.That(Draft, Is.EqualTo(originalDraft));
            Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
            return true;
        });
        Assert.That(asked && saved, Is.True);
        var stored = (await repository.LoadAsync()).Settings;
        Assert.That(stored.Presets[1].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(await repository.TryReadImageAsync(stored.Presets[1].ImageFileName), Is.EqualTo(Png));
        Assert.That(stored.Presets.Where(value => value.Id != PresetIds.Tisch),
            Is.EqualTo(initial.Presets.Where(value => value.Id != PresetIds.Tisch)));
        Assert.That(Draft.IsDirty, Is.False);
        workflow.Select(PresetIds.Leser);
        Assert.That(Draft.Settings.Name, Is.EqualTo("Leser bleibt ein Entwurf"));
        Assert.That(Draft.IsDirty, Is.True);
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoCancelPreservesTheFileImageAndExistingDraft()
    {
        await StartAsync();
        workflow!.Rename("Ungespeicherter Name");
        PresetDraft originalDraft = Draft;
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        bool saved = await workflow.CaptureAndSavePhotoAsync((confirmation, token) => Task.FromResult(false));
        Assert.That(saved, Is.False);
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft, Is.EqualTo(originalDraft));
        Assert.That(await workflow.GetDraftImageAsync(), Is.EqualTo(Png));
        Assert.That(File.Exists(OriginalImagePath), Is.True);
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoLocksSelectionAndOtherOperationsWhileDialogIsOpen()
    {
        await StartAsync(PresetIds.Tisch);
        var entered = Signal(); var release = Signal();
        Task<bool> saving = workflow!.CaptureAndSavePhotoAsync(async (confirmation, token) =>
        { entered.TrySetResult(true); return await release.Task.WaitAsync(token); });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(workflow.IsBusy, Is.True);
            Assert.Throws<InvalidOperationException>(() => workflow.Select(PresetIds.Redner));
            Assert.Throws<InvalidOperationException>(() => workflow.Rename("Fremdes Ziel"));
            Assert.Throws<InvalidOperationException>(() => workflow.Cancel());
            Assert.Throws<InvalidOperationException>(() => workflow.SaveAsync());
        }
        finally { release.TrySetResult(true); }
        Assert.That(await saving, Is.True);
        Assert.That(Draft.Settings.Id, Is.EqualTo(PresetIds.Tisch));
        Assert.That(workflow.IsBusy, Is.False);
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoHomeSavesOnlyItsImageWithoutPositionQueries()
    {
        await StartAsync(PresetIds.Home);
        await camera!.DisconnectAsync();
        client.Clear();
        bool saved = await workflow!.CaptureAndSavePhotoAsync((confirmation, token) =>
        {
            Assert.That(confirmation.Name, Is.EqualTo("Home"));
            Assert.That(confirmation.Target, Is.Null);
            return Task.FromResult(true);
        });
        Assert.That(saved, Is.True);
        var home = (await repository.LoadAsync()).Settings.Presets.Single(value => value.Id == PresetIds.Home);
        Assert.That(home.Target, Is.Null);
        Assert.That(await repository.TryReadImageAsync(home.ImageFileName), Is.EqualTo(Png));
        Assert.That(client.Requests, Is.Empty);
    }

    [TestCase(false), TestCase(true), Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoCaptureOrWriteFailureLeavesTheSavedViewAndDraftUntouched(bool writeFailure)
    {
        await StartAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft originalDraft = Draft;
        if (writeFailure)
        {
            using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAsync<SettingsStorageException>(async () => await workflow!.CaptureAndSavePhotoAsync((value, token) => Task.FromResult(true)));
        }
        else
        {
            photo.OnCapture = token => Task.FromException<byte[]>(new IOException("Own capture failure"));
            Assert.ThrowsAsync<PhotoCaptureException>(async () => await workflow!.CaptureAndSavePhotoAsync((value, token) =>
                throw new AssertionException("Ein fehlgeschlagenes Foto darf keinen Speicherdialog öffnen.")));
        }
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft, Is.EqualTo(originalDraft));
        Assert.That(File.Exists(OriginalImagePath), Is.True);
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoCallerCancellationDuringDialogDoesNotSave()
    {
        await StartAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft originalDraft = Draft;
        using var cancellation = new CancellationTokenSource();
        Assert.CatchAsync<OperationCanceledException>(async () => await workflow!.CaptureAndSavePhotoAsync((value, token) =>
        { cancellation.Cancel(); return Task.FromResult(true); }, cancellation.Token));
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft, Is.EqualTo(originalDraft));
    }

    [Test, Category("ConfirmedPhoto")]
    public async Task ConfirmedPhotoMovementDuringDialogCancelsWithoutSaving()
    {
        await StartAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft originalDraft = Draft;
        var entered = Signal();
        Task<bool> saving = workflow!.CaptureAndSavePhotoAsync(async (value, token) =>
        { entered.TrySetResult(true); await Task.Delay(Timeout.Infinite, token); return true; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await camera!.SetPanTiltAsync(24, 0);
        await camera.StopAsync();
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await saving);
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft, Is.EqualTo(originalDraft));
    }

    [Test, Category("SelectedPosition")]
    public async Task CurrentPositionSaveCommitsOnlyTheSelectedIdAndPreservesOtherDrafts()
    {
        await StartAsync();
        workflow!.Rename("Leser nicht gespeichert");
        workflow.Select(PresetIds.Tisch);
        AppSettings saved = await SaveCurrentPositionAsync();
        Assert.That(saved.Presets[1].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(saved.Presets[1].Name, Is.EqualTo("Tisch"));
        Assert.That(saved.Presets.Where(value => value.Id != PresetIds.Tisch), Is.EqualTo(initial.Presets.Where(value => value.Id != PresetIds.Tisch)));
        Assert.That((await repository.LoadAsync()).Settings.Presets, Is.EqualTo(saved.Presets));
        Assert.That(Draft.Settings.Id, Is.EqualTo(PresetIds.Tisch));
        Assert.That(Draft.IsDirty, Is.False);
        workflow.Select(PresetIds.Leser);
        Assert.That(Draft.Settings.Name, Is.EqualTo("Leser nicht gespeichert"));
        Assert.That(Draft.IsDirty, Is.True);
        Assert.That(photo.Calls, Is.Empty, "Position-only benötigt keine Vorschau.");
    }

    [TestCase(false), TestCase(true), Category("SelectedPosition")]
    public async Task CurrentPositionSaveKeepsOnlyAPhotoOfTheSameTarget(bool samePosition)
    {
        await StartAsync(samePosition: samePosition);
        AppSettings saved = await SaveCurrentPositionAsync();
        Assert.That(saved.Presets[0].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(saved.Presets[0].ImageFileName, samePosition ? Is.EqualTo(initial.Presets[0].ImageFileName) : Is.Null);
        Assert.That(File.Exists(OriginalImagePath), Is.EqualTo(samePosition));
        Assert.That(Draft.IsDirty, Is.False);
        Assert.That(photo.Calls, Is.Empty);
    }

    [TestCase("Home"), TestCase("Dirty"), TestCase("Moving"), TestCase("Disconnected"), Category("SelectedPosition")]
    public async Task CurrentPositionSaveRejectsUnsafeStateWithoutWriting(string state)
    {
        await StartAsync();
        if (state == "Home") workflow!.Select(PresetIds.Home);
        if (state == "Dirty") workflow!.Rename("Ungespeicherter Name");
        if (state == "Moving") await camera!.SetPanTiltAsync(24, 0);
        if (state == "Disconnected") await camera!.DisconnectAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft draft = Draft;
        client.Clear();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await SaveCurrentPositionAsync());
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft, Is.EqualTo(draft));
        Assert.That(client.Requests, Is.Empty);
    }

    [TestCase(false), TestCase(true), Category("SelectedPosition")]
    public async Task CurrentPositionReadOrWriteFailurePreservesSavedAndDraftState(bool failWrite)
    {
        await StartAsync();
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        PresetDraft draft = Draft;
        if (failWrite)
        {
            using var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAsync<SettingsStorageException>(async () => await SaveCurrentPositionAsync());
        }
        else
        {
            client.Intercept = (_, _) => Task.FromException<ViscaPacket>(new IOException("Own read failure"));
            Assert.ThrowsAsync<CameraOperationException>(async () => await SaveCurrentPositionAsync());
        }
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(workflow!.ActiveSettings.Presets, Is.EqualTo(initial.Presets));
        Assert.That(Draft, Is.EqualTo(draft));
        Assert.That(File.Exists(OriginalImagePath), Is.True);
    }

    [Test, Category("SelectedPosition")]
    public async Task CurrentPositionSaveBlocksSelectionThroughReadAndAtomicCommit()
    {
        await StartAsync(PresetIds.Tisch);
        await workflow!.DisposeAsync();
        var readEntered = Signal();
        var allowRead = Signal();
        var commitEntered = Signal();
        var allowCommit = Signal();
        client.Intercept = async (bytes, token) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("pan-query")))
            { readEntered.TrySetResult(true); await allowRead.Task.WaitAsync(token); }
            return ControlRecordingClient.DefaultResponse(bytes);
        };
        var delayed = new JsonSettingsRepository(dataDirectory, async () =>
        { commitEntered.TrySetResult(true); await allowCommit.Task; });
        workflow = new PresetWorkflow(delayed, camera!, photo, initial);
        workflow.Select(PresetIds.Tisch);
        Task<AppSettings> saving = SaveCurrentPositionAsync();
        try
        {
            await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(workflow.IsBusy, Is.True);
            Assert.Throws<InvalidOperationException>(() => workflow.Select(PresetIds.Redner));
            allowRead.TrySetResult(true);
            await commitEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(camera!.IsBusy, Is.False);
            Assert.That(workflow.IsBusy, Is.True, "Save muss die Auswahl auch nach dem Read sperren.");
            Assert.Throws<InvalidOperationException>(() => workflow.Select(PresetIds.Redner));
            Assert.Throws<InvalidOperationException>(() => workflow.Rename("Fremder Entwurf"));
        }
        finally { allowRead.TrySetResult(true); allowCommit.TrySetResult(true); }
        Assert.That((await saving).Presets[1].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(Draft.Settings.Id, Is.EqualTo(PresetIds.Tisch));
    }

    private Task<AppSettings> SaveCurrentPositionAsync()
    {
        return workflow!.SaveCurrentPositionAsync();
    }

    [Test, Category("SelectedPosition")]
    public async Task CurrentPositionSaveRejectsMovementBeforeCommitEvenIfStopped()
    {
        await StartAsync(PresetIds.Tisch);
        byte[] before = await File.ReadAllBytesAsync(SettingsPath);
        await workflow!.DisposeAsync();
        var staged = Signal();
        var release = Signal();
        var delayed = new JsonSettingsRepository(dataDirectory, async () =>
        { staged.TrySetResult(true); await release.Task; });
        workflow = new PresetWorkflow(delayed, camera!, photo, initial);
        workflow.Select(PresetIds.Tisch);
        Task<AppSettings> saving = SaveCurrentPositionAsync();
        try
        {
            await staged.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await camera!.SetPanTiltAsync(24, 0);
            await camera.StopAsync();
        }
        finally { release.TrySetResult(true); }
        Assert.ThrowsAsync<InvalidOperationException>(async () => await saving);
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(before));
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[1]));
        Assert.That(Draft.IsDirty, Is.False);
    }

    [Test]
    public async Task SavePhotoCommitsImageAndPositionThenReloads()
    {
        await StartAsync();
        await workflow!.CapturePhotoAsync();
        workflow.Rename("Neuer Leser");
        AppSettings saved = await workflow.SaveAsync();
        SettingsLoadResult loaded = await new JsonSettingsRepository(dataDirectory).LoadAsync();
        Assert.That(loaded.Settings.Presets[0], Is.EqualTo(saved.Presets[0]));
        Assert.That(saved.Presets[0].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(saved.Presets[0].Name, Is.EqualTo("Neuer Leser"));
        Assert.That(await repository.TryReadImageAsync(saved.Presets[0].ImageFileName), Is.EqualTo(Png));
        Assert.That(File.Exists(OriginalImagePath), Is.False);
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That(Draft.IsDirty, Is.False);
        TestContext.WriteLine("Persisted preset JSON: " + await File.ReadAllTextAsync(SettingsPath));
    }

    [Test]
    public async Task HomePhotoHasNoPositionQueriesOrTarget()
    {
        await StartAsync(PresetIds.Home);
        await workflow!.CapturePhotoAsync();
        Assert.That(client.Requests, Is.Empty);
        Assert.That(Draft.Settings.Target, Is.Null);
        Assert.That(Draft.HasNewPhoto, Is.True);
        AppSettings saved = await workflow.SaveAsync();
        Assert.That(saved.Presets[3].Target, Is.Null);
        Assert.That(await repository.TryReadImageAsync(saved.Presets[3].ImageFileName), Is.EqualTo(Png));
    }

    [Test]
    public async Task PositionWithoutPhotoRemovesOldReferenceOnlyInDraft()
    {
        await StartAsync();
        await workflow!.TakePositionAsync();
        Assert.That(Draft.Settings.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(Draft.Settings.ImageFileName, Is.Null);
        Assert.That(await workflow.GetDraftImageAsync(), Is.Null);
        Assert.That(File.Exists(OriginalImagePath), Is.True);
        Assert.That((await repository.LoadAsync()).Settings.Presets[0].ImageFileName, Is.EqualTo(initial.Presets[0].ImageFileName));
        Assert.That(photo.Calls, Is.Empty);
        await workflow.SaveAsync();
        Assert.That(File.Exists(OriginalImagePath), Is.False);
    }

    [Test]
    public async Task SamePositionWithoutPhotoKeepsOldImage()
    {
        await StartAsync(samePosition: true);
        await workflow!.TakePositionAsync();
        Assert.That(Draft.Settings.ImageFileName, Is.EqualTo(initial.Presets[0].ImageFileName));
        Assert.That(Draft.IsDirty, Is.False);
    }

    [Test]
    public async Task CancelRestoresNameTargetAndPhotoWithoutWriting()
    {
        await StartAsync();
        byte[] json = await File.ReadAllBytesAsync(SettingsPath);
        await workflow!.CapturePhotoAsync();
        workflow.Rename("Entwurf");
        workflow.Cancel();
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That(Draft.IsDirty, Is.False);
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(json));
        Assert.That(File.Exists(OriginalImagePath), Is.True);
    }

    [Test]
    public async Task SelectionAndReturnFromControlsPreserveEachDraft()
    {
        await StartAsync();
        workflow!.Rename("Leserentwurf");
        await workflow.CapturePhotoAsync();
        workflow.Select(PresetIds.Tisch);
        workflow.Rename("Tischentwurf");
        workflow.Select(PresetIds.Leser);
        workflow.Select(PresetIds.Leser);
        Assert.That(Draft.Settings.Name, Is.EqualTo("Leserentwurf"));
        Assert.That(Draft.HasNewPhoto, Is.True);
        workflow.Select(PresetIds.Tisch);
        Assert.That(Draft.Settings.Name, Is.EqualTo("Tischentwurf"));
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That(workflow.ActiveSettings.Presets, Is.EqualTo(initial.Presets));
    }

    [TestCase(false), TestCase(true)]
    public async Task VideoFailurePreservesDraftAndCameraConnection(bool failCapture)
    {
        await StartAsync();
        byte[] json = await File.ReadAllBytesAsync(SettingsPath);
        if (failCapture) photo.OnCapture = _ => throw new InvalidOperationException("rtsp://user:secret@127.0.0.1/live");
        else photo.OnOpen = _ => throw new InvalidOperationException("rtsp://user:secret@127.0.0.1/live");
        PhotoCaptureException exception = Assert.ThrowsAsync<PhotoCaptureException>(async () => await workflow!.CapturePhotoAsync())!;
        Assert.That(exception.ToString(), Does.Not.Contain("secret"));
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(json));
        Assert.That(camera!.IsConnected, Is.True);
        Assert.That(await camera.ReadPositionAsync(), Is.EqualTo(new CameraPosition(-1, 1, 4660)));
    }

    [TestCase(false), TestCase(true)]
    public async Task ChangedPositionOrZoomRejectsPhotoPair(bool changeZoom)
    {
        await StartAsync();
        int panReads = 0;
        int zoomReads = 0;
        client.Intercept = (bytes, _) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("pan-query")))
                return Task.FromResult(Fixtures.Response(++panReads == 2 && !changeZoom ? "pan-answer-zero" : "pan-answer-unit"));
            if (bytes.SequenceEqual(Fixtures.Wire("zoom-query")))
                return Task.FromResult(Fixtures.Response(++zoomReads == 2 && changeZoom ? "zoom-answer-zero" : "zoom-answer-4660"));
            return Task.FromResult(ControlRecordingClient.DefaultResponse(bytes));
        };
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await workflow!.CapturePhotoAsync());
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That((await repository.LoadAsync()).Settings.Presets[0], Is.EqualTo(initial.Presets[0]));
    }

    [Test]
    public async Task ActiveManualMovementRejectsCaptureBeforePreview()
    {
        await StartAsync();
        await camera!.SetPanTiltAsync(24, 0);
        client.Clear();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await workflow!.CapturePhotoAsync());
        Assert.That(photo.Calls, Is.Empty);
        Assert.That(client.Requests, Is.Empty);
        Assert.That(Draft.IsDirty, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public async Task ManualMotionDuringPreviewOrCaptureRejectsDraftEvenIfStopped(bool duringCapture)
    {
        await StartAsync();
        async Task MoveAndStop(CancellationToken token)
        {
            await camera!.SetPanTiltAsync(24, 0, token);
            await camera.StopAsync(token);
        }
        if (duringCapture) photo.OnCapture = async token => { await MoveAndStop(token); return (byte[])Png.Clone(); };
        else photo.OnOpen = MoveAndStop;
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await workflow!.CapturePhotoAsync());
        Assert.That(camera!.IsMoving, Is.False);
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
    }

    [Test]
    public async Task CallerCancellationChangesNoSavedOrDraftData()
    {
        await StartAsync();
        using var cancellation = new CancellationTokenSource();
        photo.OnOpen = async token => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); };
        Assert.That(async () => await workflow!.CapturePhotoAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
        Assert.That((await repository.LoadAsync()).Settings.Presets[0], Is.EqualTo(initial.Presets[0]));
    }

    [Test]
    public async Task CancelDuringLateCaptureDoesNotApplyReturnedFrame()
    {
        await StartAsync();
        var captureStarted = Signal();
        var release = Signal();
        photo.OnCapture = async _ => { captureStarted.TrySetResult(true); await release.Task; return (byte[])Png.Clone(); };
        Task capture = workflow!.CapturePhotoAsync();
        try
        {
            await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            workflow.Cancel();
        }
        finally { release.TrySetResult(true); }
        Assert.That(async () => await capture, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
    }

    [Test]
    public async Task SaveWriteFailureKeepsActiveDataAndAllowsRetry()
    {
        await StartAsync();
        await workflow!.CapturePhotoAsync();
        byte[] json = await File.ReadAllBytesAsync(SettingsPath);
        using (var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAsync<SettingsStorageException>(async () => await workflow.SaveAsync());
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(json));
        Assert.That(workflow.ActiveSettings.Presets, Is.EqualTo(initial.Presets));
        Assert.That(Draft.HasNewPhoto, Is.True);
        Assert.That(File.Exists(OriginalImagePath), Is.True);
        AppSettings saved = await workflow.SaveAsync();
        Assert.That(saved.Presets[0].Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
    }

    [Test]
    public async Task SaveMergesSelectedIdWithAlreadySavedConnectionAndWindowChanges()
    {
        await StartAsync();
        workflow!.Rename("Leser geändert");
        AppSettings updated = initial with { Connection = initial.Connection with { ViscaPort = 12345 },
            Windows = new WindowSettings(new WindowPlacement(25, 40, 600, 430, 1.5), null) };
        await repository.SaveAsync(updated);
        AppSettings saved = await workflow.SaveAsync();
        Assert.That(saved.Connection, Is.EqualTo(updated.Connection));
        Assert.That(saved.Windows, Is.EqualTo(updated.Windows));
        Assert.That(saved.Presets[0].Name, Is.EqualTo("Leser geändert"));
        Assert.That(saved.Presets.Skip(1), Is.EqualTo(updated.Presets.Skip(1)));
    }

    [Test]
    public async Task OneCaptureAtATimePreventsIdSwitch()
    {
        await StartAsync();
        var entered = Signal();
        var release = Signal();
        photo.OnOpen = async token => { entered.TrySetResult(true); await release.Task.WaitAsync(token); };
        Task capture = workflow!.CapturePhotoAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(workflow.IsBusy, Is.True);
            Assert.Throws<InvalidOperationException>(() => workflow.Select(PresetIds.Tisch));
            Assert.Throws<InvalidOperationException>(() => workflow.CapturePhotoAsync());
            workflow.Cancel();
        }
        finally { release.TrySetResult(true); }
        Assert.That(async () => await capture, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Draft.Settings.Id, Is.EqualTo(PresetIds.Leser));
    }

    [Test]
    public async Task DisposeCancelsLateCaptureAndDoesNotPublishChangesAfterwards()
    {
        await StartAsync();
        var entered = Signal();
        var release = Signal();
        photo.OnCapture = async _ => { entered.TrySetResult(true); await release.Task; return (byte[])Png.Clone(); };
        int changes = 0;
        workflow!.Changed += (_, _) => Interlocked.Increment(ref changes);
        Task capture = workflow.CapturePhotoAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ValueTask disposing = workflow.DisposeAsync();
        int countAtDisposal = changes;
        release.TrySetResult(true);
        await disposing;
        Assert.That(async () => await capture, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(changes, Is.EqualTo(countAtDisposal));
        Assert.That(camera!.IsConnected, Is.True);
        Assert.That((await repository.LoadAsync()).Settings.Presets[0], Is.EqualTo(initial.Presets[0]));
    }

    [Test]
    public async Task HomeRejectsPositionTakeAndNameChange()
    {
        await StartAsync(PresetIds.Home);
        Assert.Throws<InvalidOperationException>(() => workflow!.TakePositionAsync());
        Assert.Throws<InvalidOperationException>(() => workflow!.Rename("Raum"));
        Assert.That(client.Requests, Is.Empty);
        Assert.That(Draft.Settings.Target, Is.Null);
    }

    [Test]
    public async Task InvalidAdapterPngDoesNotChangeDraft()
    {
        await StartAsync();
        photo.OnCapture = _ => Task.FromResult(new byte[] { 1, 2, 3 });
        Assert.ThrowsAsync<PhotoCaptureException>(async () => await workflow!.CapturePhotoAsync());
        Assert.That(Draft.Settings, Is.EqualTo(initial.Presets[0]));
        Assert.That(Draft.HasNewPhoto, Is.False);
    }

    [Test]
    public async Task SaveSelectedDraftDoesNotSaveOtherDrafts()
    {
        await StartAsync();
        workflow!.Rename("Leserentwurf");
        workflow.Select(PresetIds.Tisch);
        workflow.Rename("Tischentwurf");
        AppSettings saved = await workflow.SaveAsync();
        Assert.That(saved.Presets[0].Name, Is.EqualTo("Leser"));
        Assert.That(saved.Presets[1].Name, Is.EqualTo("Tischentwurf"));
        workflow.Select(PresetIds.Leser);
        Assert.That(Draft.Settings.Name, Is.EqualTo("Leserentwurf"));
        Assert.That(Draft.IsDirty, Is.True);
    }

    [Test]
    public async Task CancelledSaveAndUnknownVersionDoNotWrite()
    {
        await StartAsync();
        workflow!.Rename("Entwurf");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await workflow.SaveAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        const string unknown = "{\"schemaVersion\":2}";
        await File.WriteAllTextAsync(SettingsPath, unknown);
        Assert.ThrowsAsync<SettingsStorageException>(async () => await workflow.SaveAsync());
        Assert.That(await File.ReadAllTextAsync(SettingsPath), Is.EqualTo(unknown));
        Assert.That(Draft.IsDirty, Is.True);
    }

    [Test]
    public async Task SaveBlocksDraftChangesAndCancellationBeforeCommitPreservesJson()
    {
        await StartAsync();
        byte[] json = await File.ReadAllBytesAsync(SettingsPath);
        await workflow!.DisposeAsync();
        var staged = Signal();
        var release = Signal();
        var delayedRepository = new JsonSettingsRepository(dataDirectory, async () =>
        {
            staged.TrySetResult(true);
            await release.Task;
        });
        workflow = new PresetWorkflow(delayedRepository, camera!, photo, initial);
        workflow.Select(PresetIds.Leser);
        workflow.Rename("Entwurf vor Speichern");
        using var cancellation = new CancellationTokenSource();
        Task<AppSettings> saving = workflow.SaveAsync(cancellation.Token);
        try
        {
            await staged.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Throws<InvalidOperationException>(() => workflow.Cancel());
            Assert.Throws<InvalidOperationException>(() => workflow.Select(PresetIds.Tisch));
            Assert.Throws<InvalidOperationException>(() => workflow.Rename("Anderer Name"));
            cancellation.Cancel();
        }
        finally { release.TrySetResult(true); }
        Assert.That(async () => await saving, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await File.ReadAllBytesAsync(SettingsPath), Is.EqualTo(json));
        Assert.That(workflow.ActiveSettings.Presets, Is.EqualTo(initial.Presets));
        Assert.That(Draft.Settings.Name, Is.EqualTo("Entwurf vor Speichern"));
        Assert.That(Draft.IsDirty, Is.True);
        Assert.That(Directory.GetFiles(dataDirectory, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task DraftOwnsPhotoBytesAndReturnedPreviewCannotChangeSavedPng()
    {
        await StartAsync();
        byte[] adapterBuffer = (byte[])Png.Clone();
        photo.OnCapture = _ => Task.FromResult(adapterBuffer);
        await workflow!.CapturePhotoAsync();
        adapterBuffer[0] = 0;
        byte[] preview = (await workflow.GetDraftImageAsync())!;
        Assert.That(preview, Is.EqualTo(Png));
        preview[0] = 0;
        AppSettings saved = await workflow.SaveAsync();
        Assert.That(await repository.TryReadImageAsync(saved.Presets[0].ImageFileName), Is.EqualTo(Png));
    }

    private sealed class TestPhotoSource : IPositionPhotoSource
    {
        private readonly List<string> calls = new();
        internal string[] Calls => calls.ToArray();
        internal Func<CancellationToken, Task>? OnOpen { get; set; }
        internal Func<CancellationToken, Task<byte[]>>? OnCapture { get; set; }
        public async Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken)
        {
            calls.Add("visible-frame");
            if (OnOpen is not null) await OnOpen(cancellationToken);
        }
        public async Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken)
        {
            calls.Add("validated-png");
            return OnCapture is null ? (byte[])Png.Clone() : await OnCapture(cancellationToken);
        }
    }
}
