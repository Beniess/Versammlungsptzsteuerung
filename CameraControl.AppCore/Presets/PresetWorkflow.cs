using CameraControl.AppCore.Control;
using CameraControl.AppCore.Storage;

namespace CameraControl.AppCore.Presets;

public interface IPositionPhotoSource
{
    Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken);
    Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken);
}

public sealed record PresetDraft(PresetSettings Settings, bool HasNewPhoto, bool IsDirty);
public sealed record PhotoConfirmation(string PresetId, string Name, ReadOnlyMemory<byte> Png, CameraPosition? Target);
public sealed class PhotoCaptureException(string message) : Exception(message);

public sealed class PresetWorkflow : IAsyncDisposable
{
    private readonly object stateLock = new();
    private readonly JsonSettingsRepository repository;
    private readonly CameraSession camera;
    private readonly IPositionPhotoSource photoSource;
    private readonly Dictionary<string, DraftEntry> drafts = new(StringComparer.Ordinal);
    private AppSettings activeSettings;
    private string? selectedId;
    private bool busy;
    private bool disposed;
    private long revision;
    private OperationKind activeKind;
    private CancellationTokenSource? operationCancellation;
    private Task currentOperation = Task.CompletedTask;
    private sealed record DraftEntry(PresetSettings Settings, byte[]? Png);
    private sealed record Snapshot(string Id, DraftEntry Entry, long Revision);
    private enum OperationKind { None, Capture, Position, Save }

    public event EventHandler? Changed;
    public AppSettings ActiveSettings { get { lock (stateLock) return activeSettings; } }
    public PresetDraft? CurrentDraft
    {
        get
        {
            lock (stateLock)
            {
                if (selectedId is null) return null;
                DraftEntry entry = drafts[selectedId];
                return new PresetDraft(entry.Settings, entry.Png is not null,
                    entry.Png is not null || entry.Settings != SavedPreset(selectedId));
            }
        }
    }
    public bool IsBusy { get { lock (stateLock) return busy; } }

    public PresetWorkflow(JsonSettingsRepository repository, CameraSession camera, IPositionPhotoSource photoSource, AppSettings initialSettings)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.photoSource = photoSource ?? throw new ArgumentNullException(nameof(photoSource));
        SettingsValidation.Validate(initialSettings);
        activeSettings = initialSettings with { Presets = Array.AsReadOnly(initialSettings.Presets.ToArray()) };
    }

    public void Select(string id)
    {
        lock (stateLock)
        {
            EnsureIdle();
            if (!PresetIds.Ordered.Contains(id)) throw new ArgumentException("Die Positions-ID ist ungültig.", nameof(id));
            if (!drafts.ContainsKey(id)) drafts.Add(id, new DraftEntry(SavedPreset(id), null));
            selectedId = id;
            revision++;
        }
        Notify();
    }

    public void Rename(string name)
    {
        lock (stateLock)
        {
            EnsureIdle();
            DraftEntry entry = SelectedEntry();
            if (selectedId == PresetIds.Home) throw new InvalidOperationException("Home hat einen festen Namen.");
            PresetSettings updated = entry.Settings with { Name = name };
            ValidatePreset(updated);
            drafts[selectedId!] = entry with { Settings = updated };
            revision++;
        }
        Notify();
    }

    public Task TakePositionAsync(CancellationToken cancellationToken = default)
    {
        lock (stateLock)
        {
            if (SelectedEntry().Settings.Id == PresetIds.Home)
                throw new InvalidOperationException("Home hat keinen lokalen Zielwert.");
            return RunOperationAsync(OperationKind.Position, async (snapshot, token) =>
            {
                EnsureCameraStill();
                CameraPosition position = await camera.ReadPositionAsync(token).ConfigureAwait(false);
                bool changed = position != snapshot.Entry.Settings.Target;
                PresetSettings updated = snapshot.Entry.Settings with
                {
                    Target = position,
                    ImageFileName = changed ? null : snapshot.Entry.Settings.ImageFileName
                };
                ValidatePreset(updated);
                Apply(snapshot, new DraftEntry(updated, changed ? null : snapshot.Entry.Png), token);
                return true;
            }, cancellationToken);
        }
    }

    public Task<AppSettings> SaveCurrentPositionAsync(CancellationToken cancellationToken = default)
    {
        lock (stateLock)
        {
            EnsureIdle();
            DraftEntry entry = SelectedEntry();
            if (entry.Settings.Id == PresetIds.Home)
                throw new InvalidOperationException("Home hat keinen lokalen Zielwert.");
            if (entry.Png is not null || entry.Settings != SavedPreset(entry.Settings.Id))
                throw new InvalidOperationException("Details zuerst speichern oder verwerfen.");
            EnsureCameraStill();
            if (!camera.IsConnected) throw new InvalidOperationException("Die Kamera ist nicht verbunden.");
            return RunOperationAsync(OperationKind.Save, SavePositionAsync, cancellationToken);
        }
    }

    private async Task<AppSettings> SavePositionAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        using var still = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int moved = 0;
        void ObserveMotion(object? sender, EventArgs args)
        {
            if (!camera.IsMoving) return;
            Interlocked.Exchange(ref moved, 1);
            still.Cancel();
        }
        camera.Changed += ObserveMotion;
        try
        {
            EnsureCameraStill();
            CameraPosition position = await camera.ReadPositionAsync(still.Token).ConfigureAwait(false);
            EnsureCameraStill();
            SettingsLoadResult latest = await repository.LoadAsync(still.Token).ConfigureAwait(false);
            if (latest.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing))
                throw new SettingsStorageException("Die gespeicherten Einstellungen benötigen eine neue Einrichtung.");
            AppSettings baseline = latest.Status == SettingsLoadStatus.Loaded ? latest.Settings : ActiveSettings;
            PresetSettings original = baseline.Presets.Single(value => value.Id == snapshot.Id);
            PresetSettings updated = original with { Target = position,
                ImageFileName = original.Target == position ? original.ImageFileName : null };
            AppSettings saved = await repository.SaveAsync(baseline with
            {
                Presets = baseline.Presets.Select(value => value.Id == snapshot.Id ? updated : value).ToArray()
            }, cancellationToken: still.Token).ConfigureAwait(false);
            lock (stateLock)
            {
                if (!disposed)
                {
                    activeSettings = saved;
                    drafts[snapshot.Id] = new DraftEntry(saved.Presets.Single(value => value.Id == snapshot.Id), null);
                    revision++;
                }
            }
            return saved;
        }
        catch (OperationCanceledException) when (Volatile.Read(ref moved) != 0)
        { throw new InvalidOperationException("Während des Speicherns wurde eine Bewegung erkannt. Der gespeicherte Stand bleibt unverändert."); }
        finally { camera.Changed -= ObserveMotion; }
    }

    public Task CapturePhotoAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(OperationKind.Capture, CaptureAsync, cancellationToken);

    public Task<bool> CaptureAndSavePhotoAsync(Func<PhotoConfirmation, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        return RunOperationAsync(OperationKind.Save, async (snapshot, token) =>
        {
            using var still = CancellationTokenSource.CreateLinkedTokenSource(token);
            int moved = 0;
            void ObserveMotion(object? sender, EventArgs args)
            {
                if (!camera.IsMoving) return;
                Interlocked.Exchange(ref moved, 1);
                still.Cancel();
            }
            camera.Changed += ObserveMotion;
            try
            {
                DraftEntry captured = await CaptureEntryAsync(snapshot, still.Token).ConfigureAwait(false);
                var confirmation = new PhotoConfirmation(snapshot.Id, captured.Settings.Name,
                    (byte[])captured.Png!.Clone(), captured.Settings.Target);
                bool accepted = await confirm(confirmation, still.Token).ConfigureAwait(false);
                still.Token.ThrowIfCancellationRequested();
                if (!accepted) return false;
                EnsureCameraStill();
                await SaveEntryAsync(snapshot with { Entry = captured }, still.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (Volatile.Read(ref moved) != 0)
            { throw new PhotoCaptureException("Während der Aufnahme oder Bestätigung wurde die Kamera bewegt. Das Foto wurde nicht gespeichert."); }
            finally { camera.Changed -= ObserveMotion; }
        }, cancellationToken);
    }

    private async Task<bool> CaptureAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        DraftEntry captured = await CaptureEntryAsync(snapshot, cancellationToken).ConfigureAwait(false);
        Apply(snapshot, captured, cancellationToken);
        return true;
    }

    private async Task<DraftEntry> CaptureEntryAsync(Snapshot snapshot, CancellationToken cancellationToken)
    {
        EnsureCameraStill();
        int observedMovement = 0;
        void ObserveMotion(object? sender, EventArgs args)
        {
            if (camera.IsMoving) Interlocked.Exchange(ref observedMovement, 1);
        }
        camera.Changed += ObserveMotion;
        try
        {
            await OpenPhotoSourceAsync(cancellationToken).ConfigureAwait(false);
            EnsureCaptureStill(Volatile.Read(ref observedMovement));
            CameraPosition? before = snapshot.Id == PresetIds.Home ? null :
                await camera.ReadPositionAsync(cancellationToken).ConfigureAwait(false);
            byte[] png = await CapturePngAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCaptureStill(Volatile.Read(ref observedMovement));
            CameraPosition? after = snapshot.Id == PresetIds.Home ? null :
                await camera.ReadPositionAsync(cancellationToken).ConfigureAwait(false);
            EnsureCaptureStill(Volatile.Read(ref observedMovement));
            if (before != after) throw new PhotoCaptureException("Die Kamera hat sich während der Aufnahme bewegt. Die gespeicherte Ansicht bleibt unverändert.");
            PresetSettings updated = snapshot.Entry.Settings with { Target = before, ImageFileName = null };
            ValidatePreset(updated);
            return new DraftEntry(updated, png);
        }
        finally { camera.Changed -= ObserveMotion; }
    }

    private async Task OpenPhotoSourceAsync(CancellationToken cancellationToken)
    {
        try { await photoSource.OpenAndWaitForFrameAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new PhotoCaptureException("Die Vorschau liefert kein lesbares Bild. Die gespeicherte Ansicht bleibt unverändert."); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<byte[]> CapturePngAsync(CancellationToken cancellationToken)
    {
        byte[]? image;
        try { image = await photoSource.CaptureValidatedPngAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new PhotoCaptureException("Das Foto konnte nicht aufgenommen werden. Die gespeicherte Ansicht bleibt unverändert."); }
        cancellationToken.ThrowIfCancellationRequested();
        if (image is null) throw new PhotoCaptureException("Die Aufnahme hat keine PNG-Daten geliefert.");
        byte[] owned = (byte[])image.Clone();
        if (!PngIntegrity.IsComplete(owned)) throw new PhotoCaptureException("Die Aufnahme ist keine vollständige PNG-Datei.");
        return owned;
    }

    public Task<byte[]?> GetDraftImageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DraftEntry entry = SelectedEntry();
            return entry.Png is null ? repository.TryReadImageAsync(entry.Settings.ImageFileName, cancellationToken) :
                Task.FromResult<byte[]?>((byte[])entry.Png.Clone());
        }
    }

    public Task<AppSettings> SaveAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(OperationKind.Save, SaveEntryAsync, cancellationToken);

    private async Task<AppSettings> SaveEntryAsync(Snapshot snapshot, CancellationToken token)
    {
            SettingsLoadResult latest = await repository.LoadAsync(token).ConfigureAwait(false);
            if (latest.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing))
                throw new SettingsStorageException("Die gespeicherten Einstellungen können nicht ersetzt werden. Neue Einrichtung ist erforderlich.");
            AppSettings baseline = latest.Status == SettingsLoadStatus.Loaded ? latest.Settings : ActiveSettings;
            AppSettings updated = baseline with
            {
                Presets = baseline.Presets.Select(preset => preset.Id == snapshot.Id ? snapshot.Entry.Settings : preset).ToArray()
            };
            IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? images = snapshot.Entry.Png is null ? null :
                new Dictionary<string, ReadOnlyMemory<byte>> { [snapshot.Id] = snapshot.Entry.Png };
            AppSettings saved = await repository.SaveAsync(updated, images, token).ConfigureAwait(false);
            lock (stateLock)
            {
                if (!disposed)
                {
                    activeSettings = saved;
                    drafts[snapshot.Id] = new DraftEntry(saved.Presets.Single(preset => preset.Id == snapshot.Id), null);
                    revision++;
                }
            }
            return saved;
    }

    public void Cancel()
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            SelectedEntry();
            if (busy && activeKind == OperationKind.Save) throw new InvalidOperationException("Speichern läuft. Warte auf das Ergebnis.");
            operationCancellation?.Cancel();
            drafts[selectedId!] = new DraftEntry(SavedPreset(selectedId!), null);
            revision++;
        }
        Notify();
    }

    private Task<T> RunOperationAsync<T>(OperationKind kind, Func<Snapshot, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            EnsureIdle();
            DraftEntry entry = SelectedEntry();
            var snapshot = new Snapshot(selectedId!, entry, revision);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            busy = true;
            activeKind = kind;
            operationCancellation = cancellation;
            Task<T> task = ExecuteAsync(snapshot, cancellation, operation);
            currentOperation = task;
            Notify();
            return task;
        }
    }

    private async Task<T> ExecuteAsync<T>(Snapshot snapshot, CancellationTokenSource cancellation,
        Func<Snapshot, CancellationToken, Task<T>> operation)
    {
        await Task.Yield();
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            return await operation(snapshot, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (stateLock)
            {
                busy = false;
                activeKind = OperationKind.None;
                operationCancellation = null;
                currentOperation = Task.CompletedTask;
            }
            cancellation.Dispose();
            Notify();
        }
    }

    private void Apply(Snapshot snapshot, DraftEntry updated, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed || revision != snapshot.Revision || selectedId != snapshot.Id)
                throw new OperationCanceledException("Der Positionsentwurf wurde nicht übernommen.", cancellationToken);
            drafts[snapshot.Id] = updated;
            revision++;
        }
    }

    private void EnsureCameraStill()
    {
        if (camera.IsMoving || camera.IsBusy) throw new InvalidOperationException("Während einer Bewegung oder eines Kameraauftrags ist keine Aufnahme möglich.");
    }

    private void EnsureCaptureStill(int observedMovement)
    {
        if (observedMovement != 0 || camera.IsMoving || camera.IsBusy)
            throw new PhotoCaptureException("Während der Aufnahme wurde die Kamera bewegt. Die gespeicherte Ansicht bleibt unverändert.");
    }

    private void EnsureIdle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (busy) throw new InvalidOperationException("Ein Positionsauftrag läuft bereits.");
    }

    private DraftEntry SelectedEntry() => selectedId is null ?
        throw new InvalidOperationException("Wähle zuerst eine Position.") : drafts[selectedId];
    private PresetSettings SavedPreset(string id) => activeSettings.Presets.Single(preset => preset.Id == id);
    private void ValidatePreset(PresetSettings preset) => SettingsValidation.Validate(activeSettings with
    {
        Presets = activeSettings.Presets.Select(value => value.Id == preset.Id ? preset : value).ToArray()
    });

    private void Notify()
    {
        lock (stateLock)
            if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (stateLock)
        {
            disposed = true;
            operationCancellation?.Cancel();
            pending = currentOperation;
        }
        await Task.WhenAny(pending).ConfigureAwait(false);
        if (pending.IsFaulted) _ = pending.Exception;
    }
}
