using System.Net;
using CameraControl.AppCore;
using CameraControl.AppCore.Control;
using CameraControl.AppCore.Presets;
using CameraControl.AppCore.Storage;

namespace CameraControl_WinUI;

public sealed partial class ApplicationServices : IAsyncDisposable
{
    private readonly SemaphoreSlim settingsLock = new(1, 1);
    private readonly object stateLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly PhotoSourceProxy photoSource;
    private AppSettings settings = AppSettings.CreateDefault();
    private bool readOnly;
    private bool closing;
    private bool initialized;
    private Task initialization = Task.CompletedTask;
    private Task autoConnect = Task.CompletedTask;
    private Task? closeTask;
    private readonly HashSet<Task> foregroundTasks = [];
    private bool closed;
    private Func<CancellationToken, Task>? openPreview;
    private Func<ValueTask>? disposePreview;
    private string videoStatus = "Nicht geöffnet";
    private bool videoError;
    private PresetWorkflow? presets;
    private bool selectedPositionSaving;
    private bool selectedPhotoSaving;

    public event EventHandler? Changed;
    public JsonSettingsRepository Repository { get; }
    public CameraSession Camera { get; } = new();
    public string DataDirectory { get; }
    public bool IsQa { get; }
    public bool IsReadOnly { get { lock (stateLock) return readOnly; } }
    public bool IsClosing { get { lock (stateLock) return closing; } }
    public bool IsInitialized { get { lock (stateLock) return initialized; } }
    public bool CanOperate => IsInitialized && !IsReadOnly && !IsClosing && !NeedsNewSetup && !IsConnectionChanging;
    public AppSettings Settings { get { lock (stateLock) return settings; } }
    public SettingsLoadStatus LoadStatus { get; private set; } = SettingsLoadStatus.Missing;
    public string? LoadMessage { get; private set; }
    public bool NeedsNewSetup => LoadStatus is not (SettingsLoadStatus.Missing or SettingsLoadStatus.Loaded);
    public PresetWorkflow Presets => presets ?? throw new InvalidOperationException("Die Einstellungen sind noch nicht geladen.");
    public string VideoStatus { get { lock (stateLock) return videoStatus; } }
    public bool VideoHasError { get { lock (stateLock) return videoError; } }
    public string? LastShutdownError { get; private set; }
    public CancellationToken LifetimeToken => lifetime.Token;
    public string? SelectedPresetId => IsInitialized ? Presets.CurrentDraft?.Settings.Id : null;
    public string SelectedPresetName => IsInitialized ? Presets.CurrentDraft?.Settings.Name ?? "" : "";
    public bool IsSavingSelectedPosition { get { lock (stateLock) return selectedPositionSaving; } }
    public bool IsSavingSelectedPhoto { get { lock (stateLock) return selectedPhotoSaving; } }
    private bool HasForegroundTasks { get { lock (stateLock) return foregroundTasks.Any(task => !task.IsCompleted); } }
    public bool CanSelectPreset => CanOperate && !IsSavingSelectedPosition && !IsSavingSelectedPhoto && !HasForegroundTasks &&
        !Camera.IsBusy && !Camera.IsMoving && !Presets.IsBusy;
    public string? CanSaveSelectedPositionReason => !CanSelectPreset ? "Die Kamera ist nicht bereit oder ein Auftrag läuft."
        : !Camera.IsConnected ? "Die Kamera ist nicht verbunden."
        : SelectedPresetId == PresetIds.Home ? "Home hat keinen lokalen Zielwert."
        : Presets.CurrentDraft is null ? "Wähle zuerst eine Position."
        : Presets.CurrentDraft.IsDirty ? "Details zuerst speichern oder verwerfen." : null;
    public bool CanSaveSelectedPosition => CanSaveSelectedPositionReason is null;
    public string? CanTakeSelectedPhotoReason => !CanSelectPreset ? "Warte, bis der laufende Auftrag beendet ist."
        : string.IsNullOrWhiteSpace(Settings.Connection.RtspUrl) ? "Für Fotos die Livebild-Adresse unter Verbindung eintragen."
        : SelectedPresetId is null ? "Wähle zuerst eine Ansicht."
        : SelectedPresetId != PresetIds.Home && !Camera.IsConnected ? "Verbinde zuerst die Kamera." : null;
    public bool CanTakeSelectedPhoto => CanTakeSelectedPhotoReason is null;

    public Task<bool> SaveSelectedPhotoAsync(Func<PhotoConfirmation, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        lock (stateLock)
        {
            if (CanTakeSelectedPhotoReason is { } reason) throw new InvalidOperationException(reason);
            selectedPhotoSaving = true;
            try { return OwnCameraTask(token => SaveSelectedPhotoCoreAsync(confirm, token), cancellationToken); }
            catch { selectedPhotoSaving = false; throw; }
        }
    }

    private async Task<bool> SaveSelectedPhotoCoreAsync(Func<PhotoConfirmation, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken)
    {
        Notify();
        bool ownsLock = false;
        try
        {
            await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            ownsLock = true;
            EnsureCanOperate();
            bool saved = await Presets.CaptureAndSavePhotoAsync(confirm, cancellationToken).ConfigureAwait(false);
            if (saved)
                lock (stateLock) { settings = Presets.ActiveSettings; LoadStatus = SettingsLoadStatus.Loaded; LoadMessage = null; }
            return saved;
        }
        finally
        {
            if (ownsLock) settingsLock.Release();
            lock (stateLock) selectedPhotoSaving = false;
            Notify();
        }
    }

    public void SelectPreset(string id)
    {
        lock (stateLock)
        {
            if (!CanSelectPreset) throw new InvalidOperationException("Die Auswahl ist während eines Auftrags, einer Bewegung oder beim Schließen gesperrt.");
            Presets.Select(id);
        }
    }

    private ApplicationServices(string dataDirectory, bool readOnly, bool isQa, IPositionPhotoSource? source)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        var productDirectory = Path.TrimEndingDirectorySeparator(WindowsDataPath.GetDirectory());
        if (isQa && (DataDirectory.Equals(productDirectory, StringComparison.OrdinalIgnoreCase) ||
            DataDirectory.StartsWith(productDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Der QA-Datenpfad muss außerhalb des regulären Produktdatenordners liegen.", nameof(dataDirectory));
        Repository = new JsonSettingsRepository(DataDirectory);
        this.readOnly = readOnly;
        IsQa = isQa;
        photoSource = new PhotoSourceProxy(this, source);
        Camera.Changed += OnChanged;
    }

    public static async Task<ApplicationServices> CreateAsync(string dataDirectory, bool readOnly,
        IPositionPhotoSource? photoSource = null, CancellationToken cancellationToken = default)
    {
        return await CreateAsync(dataDirectory, readOnly, isQa: false, photoSource, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ApplicationServices> CreateAsync(string dataDirectory, bool readOnly, bool isQa,
        IPositionPhotoSource? photoSource = null, CancellationToken cancellationToken = default)
    {
        var services = new ApplicationServices(dataDirectory, readOnly, isQa, photoSource);
        await services.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return services;
    }

    public static ApplicationServices CreateDevelopment()
    {
        var qaDirectory = Environment.GetEnvironmentVariable("CAMERA_CONTROL_QA_DATA_DIRECTORY");
        var isolatedDirectory = string.IsNullOrWhiteSpace(qaDirectory)
            ? Path.Combine(Path.GetTempPath(), "Versammlungsptzsteuerung-Development", "Data") : qaDirectory;
        return new ApplicationServices(isolatedDirectory, readOnly: false, isQa: true, source: null);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (stateLock)
        {
            if (closing) return Task.FromException(new ObjectDisposedException(nameof(ApplicationServices)));
            if (initialized) return Task.CompletedTask;
            if (!initialization.IsCompleted) return initialization.WaitAsync(cancellationToken);
            initialization = LoadInitialAsync(cancellationToken);
            return initialization;
        }
    }

    private async Task LoadInitialAsync(CancellationToken cancellationToken)
    {
        await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await Repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (stateLock)
            {
                settings = loaded.Settings;
                LoadStatus = loaded.Status;
                LoadMessage = loaded.Message;
                presets = new PresetWorkflow(Repository, Camera, photoSource, settings);
                presets.Changed += OnChanged;
                presets.Select(PresetIds.Leser);
                initialized = true;
            }
        }
        finally { settingsLock.Release(); }
        Notify();
    }

    public Task ActivateCommittedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            if (closing) throw new InvalidOperationException("Die Anwendung wird geschlossen.");
            if (!initialized) throw new InvalidOperationException("Die Oberfläche ist noch nicht vorbereitet.");
            if (!readOnly) return Task.CompletedTask;
            readOnly = false;
        }
        Notify();
        if (CanOperate && Settings.Connection.IsConfigured)
            autoConnect = ConnectAfterCommitAsync();
        return Task.CompletedTask;
    }

    private async Task ConnectAfterCommitAsync()
    {
        try { await ConnectAsync(Settings.Connection, lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is CameraOperationException or OperationCanceledException or InvalidOperationException)
        { Notify(); }
    }

    public void ConfigurePreview(IPositionPhotoSource source, Func<CancellationToken, Task> openPreviewAsync,
        Func<ValueTask> disposePreviewAsync)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(openPreviewAsync);
        ArgumentNullException.ThrowIfNull(disposePreviewAsync);
        photoSource.Source = source;
        openPreview = openPreviewAsync;
        disposePreview = disposePreviewAsync;
    }

    public void UpdateVideoStatus(string message, bool isError)
    {
        lock (stateLock)
        {
            if (closing) return;
            videoStatus = message;
            videoError = isError;
        }
        Notify();
    }

    public async Task OpenPreviewAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanOperate();
        if (string.IsNullOrWhiteSpace(Settings.Connection.RtspUrl))
            throw new InvalidOperationException("Trage zuerst eine vollständige RTSP-URL in den Einstellungen ein.");
        ValidatePreviewEndpoint(new Uri(Settings.Connection.RtspUrl));
        if (openPreview is null) throw new InvalidOperationException("Die Vorschau ist noch nicht bereit.");
        await openPreview(cancellationToken).ConfigureAwait(false);
    }

    public Task ConnectAsync(ConnectionSettings connection, CancellationToken cancellationToken = default)
    {
        EnsureCanOperate();
        EnsureQaEndpoint(connection.Host);
        return OwnCameraTask(token => Task.Run(() => Camera.ConnectAsync(connection, token), token), cancellationToken);
    }

    public Task<bool> RecallAsync(PresetSettings preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        lock (stateLock)
        {
            EnsureCanOperate();
            var stored = Settings.Presets.FirstOrDefault(value => value.Id == preset.Id);
            if (stored is null || stored != preset) throw new ArgumentException("Die Position gehört nicht zum gespeicherten Stand.", nameof(preset));
            if (!Camera.IsConnected) throw new InvalidOperationException("Die Kamera ist nicht verbunden.");
            SelectPreset(stored.Id);
            return OwnCameraTask(token => Camera.RecallAsync(stored, token), cancellationToken);
        }
    }

    public Task<AppSettings> SaveSelectedPositionAsync(CancellationToken cancellationToken = default)
    {
        lock (stateLock)
        {
            if (CanSaveSelectedPositionReason is { } reason) throw new InvalidOperationException(reason);
            string selectedId = SelectedPresetId!;
            selectedPositionSaving = true;
            try { return OwnCameraTask(token => SaveSelectedPositionCoreAsync(selectedId, token), cancellationToken); }
            catch { selectedPositionSaving = false; throw; }
        }
    }

    private async Task<AppSettings> SaveSelectedPositionCoreAsync(string selectedId, CancellationToken cancellationToken)
    {
        Notify();
        bool ownsLock = false;
        try
        {
            await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            ownsLock = true;
            EnsureCanOperate();
            if (SelectedPresetId != selectedId) throw new InvalidOperationException("Die gewählte Position hat sich geändert.");
            var saved = await Presets.SaveCurrentPositionAsync(cancellationToken).ConfigureAwait(false);
            lock (stateLock) { settings = saved; LoadStatus = SettingsLoadStatus.Loaded; LoadMessage = null; }
            return saved;
        }
        finally
        {
            if (ownsLock) settingsLock.Release();
            lock (stateLock) selectedPositionSaving = false;
            Notify();
        }
    }

    public Task<CameraPosition> ReadPositionAsync(CancellationToken cancellationToken = default) =>
        OwnCameraTask(token => Camera.ReadPositionAsync(token), cancellationToken);

    public Task SetPanTiltAsync(int pan, int tilt, CancellationToken cancellationToken = default) =>
        OwnManualTask(token => Camera.SetPanTiltAsync(pan, tilt, token), cancellationToken);

    public Task SetZoomAsync(int zoom, CancellationToken cancellationToken = default) =>
        OwnManualTask(token => Camera.SetZoomAsync(zoom, token), cancellationToken);

    private Task OwnManualTask(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            EnsureCanOperate();
            if (selectedPositionSaving || selectedPhotoSaving || Presets.IsBusy) throw new InvalidOperationException("Während eines Positions- oder Fotoauftrags ist die Bewegung gesperrt.");
            return OwnCameraTask(operation, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        OwnCameraTask(token => Camera.StopAsync(token), cancellationToken);

    private Task OwnCameraTask(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            EnsureCanOperate();
            var linked = cancellationToken.CanBeCanceled && cancellationToken != lifetime.Token
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token) : null;
            try { return Track(operation(linked?.Token ?? lifetime.Token), linked); }
            catch { linked?.Dispose(); throw; }
        }
    }

    private Task<T> OwnCameraTask<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            EnsureCanOperate();
            var linked = cancellationToken.CanBeCanceled && cancellationToken != lifetime.Token
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token) : null;
            try { var task = operation(linked?.Token ?? lifetime.Token); Track(task, linked); return task; }
            catch { linked?.Dispose(); throw; }
        }
    }

    private Task Track(Task task, CancellationTokenSource? linked)
    {
        if (foregroundTasks.Add(task))
            _ = task.ContinueWith(completed => { lock (stateLock) foregroundTasks.Remove(completed); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (linked is not null)
            _ = task.ContinueWith(_ => linked.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    public async Task SaveConnectionAsync(ConnectionSettings connection, CancellationToken cancellationToken = default)
    {
        await SaveConnectionWithCredentialsAsync(connection, null, null, false, cancellationToken).ConfigureAwait(false);
    }

    public Task SaveMainPlacementAsync(WindowPlacement? placement, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(latest => latest with { Windows = latest.Windows with { Main = placement } }, cancellationToken, allowClosing: true);

    public Task SavePreviewPlacementAsync(WindowPlacement? placement, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(latest => latest with { Windows = latest.Windows with { Preview = placement } }, cancellationToken, allowClosing: true);

    private async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken, bool allowClosing = false)
    {
        EnsureCanWrite(allowClosing);
        await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanWrite(allowClosing);
            var loaded = await Repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing))
                throw new SettingsStorageException("Die Einstellungsdatei benötigt eine ausdrückliche neue Einrichtung.");
            var baseline = loaded.Status == SettingsLoadStatus.Loaded ? loaded.Settings : Settings;
            var saved = await Repository.SaveAsync(change(baseline), cancellationToken: cancellationToken).ConfigureAwait(false);
            lock (stateLock) { settings = saved; LoadStatus = SettingsLoadStatus.Loaded; LoadMessage = null; }
        }
        finally { settingsLock.Release(); }
        Notify();
    }

    public async Task<AppSettings> SavePresetAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanOperate();
        if (IsSavingSelectedPosition || IsSavingSelectedPhoto) throw new InvalidOperationException("Die gewählte Ansicht wird bereits gespeichert.");
        await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCanOperate();
            if (IsSavingSelectedPosition || IsSavingSelectedPhoto) throw new InvalidOperationException("Die gewählte Ansicht wird bereits gespeichert.");
            var saved = await Presets.SaveAsync(cancellationToken).ConfigureAwait(false);
            lock (stateLock) { settings = saved; LoadStatus = SettingsLoadStatus.Loaded; LoadMessage = null; }
            Notify();
            return saved;
        }
        finally { settingsLock.Release(); }
    }

    private Task OwnDraftTask(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            if (selectedPositionSaving || selectedPhotoSaving) throw new InvalidOperationException("Die gewählte Ansicht wird bereits gespeichert.");
            return OwnCameraTask(operation, cancellationToken);
        }
    }

    public async Task ResetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (IsReadOnly || IsClosing) throw new InvalidOperationException("Die Einrichtung ist noch nicht freigegeben.");
        await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var saved = await Repository.ResetAsync(cancellationToken).ConfigureAwait(false);
            if (presets is not null) { presets.Changed -= OnChanged; await presets.DisposeAsync().ConfigureAwait(false); }
            lock (stateLock)
            {
                settings = saved;
                connectionRevision++;
                LoadStatus = SettingsLoadStatus.Loaded;
                LoadMessage = null;
                presets = new PresetWorkflow(Repository, Camera, photoSource, saved);
                presets.Changed += OnChanged;
                presets.Select(PresetIds.Leser);
            }
        }
        finally { settingsLock.Release(); }
        Notify();
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            closing = true;
            return closeTask ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        await Task.Yield();
        Notify();
        try
        {
            if (Camera.IsConnected && Camera.IsMoving && !Camera.IsBusy)
                await Camera.StopAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is CameraOperationException or OperationCanceledException or InvalidOperationException)
        { LastShutdownError = "Stopp konnte nicht bestätigt werden. " + error.Message; }
        finally { lifetime.Cancel(); }
        Task[] pending;
        lock (stateLock) pending = foregroundTasks.Append(autoConnect).Append(initialization).ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (Exception error) when (UserOperation.IsHandled(error))
        { LastShutdownError ??= error.Message; }
        if (presets is not null)
        {
            presets.Changed -= OnChanged;
            await presets.DisposeAsync().ConfigureAwait(false);
        }
        await settingsLock.WaitAsync().ConfigureAwait(false);
        settingsLock.Release();
        try { if (disposePreview is not null) await disposePreview().ConfigureAwait(false); }
        finally
        {
            Camera.Changed -= OnChanged;
            try { await Camera.DisposeAsync().ConfigureAwait(false); }
            finally { lock (stateLock) closed = true; }
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private void EnsureCanOperate()
    {
        if (!CanOperate) throw new InvalidOperationException(IsReadOnly
            ? "Die neue Programmversion ist noch nicht aktiviert."
            : NeedsNewSetup ? "Die Einstellungen benötigen eine neue Einrichtung." : "Die Anwendung ist noch nicht bereit oder wird geschlossen.");
    }

    private void EnsureCanWrite(bool allowClosing)
    {
        lock (stateLock)
            if (closed || !initialized || readOnly || NeedsNewSetup || closing && !allowClosing)
                throw new InvalidOperationException("Die Einstellungen sind noch nicht für Schreibzugriffe freigegeben.");
    }

    public void ValidatePreviewEndpoint(Uri address) => EnsureQaEndpoint(address.DnsSafeHost);

    private void EnsureQaEndpoint(string? host)
    {
        if (!IsQa) return;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)) return;
        throw new InvalidOperationException("Der QA-Modus erlaubt nur eine lokale Testkamera auf Loopback.");
    }

    private void OnChanged(object? sender, EventArgs args) => Notify();
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class PhotoSourceProxy(ApplicationServices owner, IPositionPhotoSource? source) : IPositionPhotoSource
    {
        public IPositionPhotoSource? Source { get; set; } = source;
        private IPositionPhotoSource Current
        {
            get
            {
                owner.EnsureCanOperate();
                if (owner.Settings.Connection.RtspUrl is { } rtsp) owner.ValidatePreviewEndpoint(new Uri(rtsp));
                return Source ?? throw new PhotoCaptureException("Die Vorschau ist noch nicht bereit.");
            }
        }
        public Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken) => Current.OpenAndWaitForFrameAsync(cancellationToken);
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken) => Current.CaptureValidatedPngAsync(cancellationToken);
    }
}
