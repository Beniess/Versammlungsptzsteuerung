using CameraControl.Visca;
using CameraControl.AppCore.Storage;

namespace CameraControl.AppCore.Control;

public sealed class CameraOperationException(string message, Exception innerException) : Exception(message, innerException);

public sealed class CameraSession : IAsyncDisposable
{
    private readonly object stateLock = new();
    private readonly SemaphoreSlim wireLock = new(1, 1);
    private readonly Func<ConnectionSettings, ViscaController> factory;
    private ViscaController? controller;
    private bool connected;
    private bool exclusiveBusy;
    private bool stopping;
    private bool pumpRunning;
    private bool disposed;
    private int panSpeed;
    private int tiltSpeed;
    private int zoomSpeed;
    private string? lastError;
    private PanRequest? pendingPan;
    private ZoomRequest? pendingZoom;
    private PanRequest? activePan;
    private ZoomRequest? activeZoom;
    private Task manualTask = Task.CompletedTask;
    private readonly record struct PanRequest(int Pan, int Tilt, CancellationToken Token);
    private readonly record struct ZoomRequest(int Zoom, CancellationToken Token);

    public event EventHandler? Changed;
    public bool IsConnected { get { lock (stateLock) return connected; } }
    public bool IsBusy { get { lock (stateLock) return exclusiveBusy || stopping; } }
    public bool IsMoving { get { lock (stateLock) return MovingLocked; } }
    public int PanSpeed { get { lock (stateLock) return panSpeed; } }
    public int TiltSpeed { get { lock (stateLock) return tiltSpeed; } }
    public int ZoomSpeed { get { lock (stateLock) return zoomSpeed; } }
    public string? LastError { get { lock (stateLock) return lastError; } }

    public CameraSession(TimeSpan? commandTimeout = null) : this(settings =>
        ViscaController.ForTcp(settings.Host!, settings.ViscaPort, commandTimeout)) { }

    internal CameraSession(Func<ConnectionSettings, ViscaController> factory) =>
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        SettingsValidation.Validate(AppSettings.CreateDefault() with { Connection = settings });
        if (!settings.IsConfigured) throw new InvalidOperationException("Der Kamerahost fehlt.");
        BeginExclusive(needsConnection: false, allowManual: true, rejectBusy: false);
        Notify();
        try
        {
            Task stop;
            lock (stateLock) stop = connected ? QueueStopLocked(cancellationToken) : Task.CompletedTask;
            await stop.ConfigureAwait(false);
            DropController(null);
            ViscaController next = factory(settings);
            lock (stateLock) controller = next;
            await wireLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                PowerStatus status = await next.GetPowerStatus(cancellationToken).ConfigureAwait(false);
                if (!Enum.IsDefined(status)) throw new ViscaProtocolException("Ungültige Antwort auf die Verbindungsprüfung.");
            }
            finally { wireLock.Release(); }
            lock (stateLock) { connected = true; lastError = null; }
        }
        catch (Exception exception) { throw DisconnectForFailure(exception, cancellationToken); }
        finally { EndExclusive(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (stateLock)
        {
            if (disposed) return;
        }
        BeginExclusive(needsConnection: false, allowManual: true, rejectBusy: false);
        Notify();
        try
        {
            Task stop;
            lock (stateLock) stop = connected ? QueueStopLocked(cancellationToken) : Task.CompletedTask;
            await stop.ConfigureAwait(false);
        }
        finally { DropController(null); EndExclusive(); }
    }

    public async Task<CameraPosition> ReadPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginExclusive(needsConnection: true, allowManual: false, rejectBusy: false);
        Notify();
        try
        {
            await wireLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ViscaController current = GetController();
                var (pan, tilt) = await current.GetPanTilt(cancellationToken).ConfigureAwait(false);
                short zoom = await current.GetZoom(cancellationToken).ConfigureAwait(false);
                return new CameraPosition(pan, tilt, zoom);
            }
            finally { wireLock.Release(); }
        }
        catch (Exception exception) { throw DisconnectForFailure(exception, cancellationToken); }
        finally { EndExclusive(); }
    }

    public async Task<bool> RecallAsync(PresetSettings preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PresetIds.Ordered.Contains(preset.Id)) throw new ArgumentException("Die Positions-ID ist ungültig.", nameof(preset));
        if (preset.Id != PresetIds.Home && preset.Target is null) return false;
        if (preset.Target is { Zoom: < 0 } || preset.Id == PresetIds.Home && preset.Target is not null)
            throw new ArgumentException("Der Positionswert ist ungültig.", nameof(preset));
        if (!BeginExclusive(needsConnection: true, allowManual: false, rejectBusy: true)) return false;
        Notify();
        try
        {
            await wireLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ViscaController current = GetController();
                if (preset.Id == PresetIds.Home) await current.GoHome(cancellationToken).ConfigureAwait(false);
                else
                {
                    CameraPosition target = preset.Target!;
                    await current.SetPanTilt(target.Pan, target.Tilt, 12, 12, cancellationToken).ConfigureAwait(false);
                    await current.SetZoom(target.Zoom, cancellationToken).ConfigureAwait(false);
                }
                return true;
            }
            finally { wireLock.Release(); }
        }
        catch (Exception exception) { throw DisconnectForFailure(exception, cancellationToken); }
        finally { EndExclusive(); }
    }

    public Task SetPanTiltAsync(int pan, int tilt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pan is < -24 or > 24 || tilt is < -24 or > 24) throw new ArgumentOutOfRangeException(nameof(pan));
        Task result;
        lock (stateLock)
        {
            EnsureManualAllowed();
            PanRequest latest = pendingPan ?? activePan ?? new PanRequest(panSpeed, tiltSpeed, default);
            if (latest.Pan == pan && latest.Tilt == tilt) return pumpRunning ? manualTask : Task.CompletedTask;
            pendingPan = new PanRequest(pan, tilt, cancellationToken);
            result = StartPumpLocked();
        }
        Notify();
        return result;
    }

    public Task SetZoomAsync(int zoom, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (zoom is < -7 or > 7) throw new ArgumentOutOfRangeException(nameof(zoom));
        Task result;
        lock (stateLock)
        {
            EnsureManualAllowed();
            int latest = pendingZoom?.Zoom ?? activeZoom?.Zoom ?? zoomSpeed;
            if (latest == zoom) return pumpRunning ? manualTask : Task.CompletedTask;
            pendingZoom = new ZoomRequest(zoom, cancellationToken);
            result = StartPumpLocked();
        }
        Notify();
        return result;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task result;
        lock (stateLock)
        {
            EnsureConnected();
            if (exclusiveBusy) throw new InvalidOperationException("Ein Kameraauftrag läuft bereits.");
            result = QueueStopLocked(cancellationToken);
        }
        Notify();
        return result;
    }

    private Task QueueStopLocked(CancellationToken cancellationToken)
    {
        bool stopPan = panSpeed != 0 || tiltSpeed != 0 || activePan is { } pan && (pan.Pan != 0 || pan.Tilt != 0);
        bool stopZoom = zoomSpeed != 0 || activeZoom is { Zoom: not 0 };
        pendingPan = stopPan ? new PanRequest(0, 0, cancellationToken) : null;
        pendingZoom = stopZoom ? new ZoomRequest(0, cancellationToken) : null;
        stopping = pumpRunning || stopPan || stopZoom;
        return stopping ? StartPumpLocked() : Task.CompletedTask;
    }

    private Task StartPumpLocked()
    {
        if (!pumpRunning)
        {
            pumpRunning = true;
            manualTask = Task.Run(RunManualAsync);
        }
        return manualTask;
    }

    private async Task RunManualAsync()
    {
        CancellationToken currentToken = default;
        try
        {
            while (true)
            {
                PanRequest? pan;
                ZoomRequest? zoom;
                lock (stateLock)
                {
                    pan = pendingPan;
                    zoom = pan is null ? pendingZoom : null;
                    if (pan is not null) { pendingPan = null; activePan = pan; }
                    else if (zoom is not null) { pendingZoom = null; activeZoom = zoom; }
                    else { pumpRunning = false; stopping = false; break; }
                }
                currentToken = pan?.Token ?? zoom!.Value.Token;
                await wireLock.WaitAsync(currentToken).ConfigureAwait(false);
                try
                {
                    ViscaController current = GetController();
                    if (pan is { } p)
                    {
                        bool changed;
                        lock (stateLock) changed = p.Pan != panSpeed || p.Tilt != tiltSpeed;
                        if (changed)
                            await current.ContinuousPanTilt(Direction(p.Pan), Direction(p.Tilt), Speed(p.Pan), Speed(p.Tilt), currentToken).ConfigureAwait(false);
                        lock (stateLock) { panSpeed = p.Pan; tiltSpeed = p.Tilt; activePan = null; }
                    }
                    else if (zoom is { } z)
                    {
                        bool changed;
                        lock (stateLock) changed = z.Zoom != zoomSpeed;
                        if (changed) await current.ContinuousZoom(Direction(z.Zoom), Speed(z.Zoom), currentToken).ConfigureAwait(false);
                        lock (stateLock) { zoomSpeed = z.Zoom; activeZoom = null; }
                    }
                }
                finally { wireLock.Release(); }
                Notify();
            }
            Notify();
        }
        catch (Exception exception)
        {
            throw DisconnectForFailure(exception, currentToken);
        }
    }

    private bool BeginExclusive(bool needsConnection, bool allowManual, bool rejectBusy)
    {
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (exclusiveBusy || stopping || !allowManual && (pumpRunning || MovingLocked))
            {
                if (rejectBusy) return false;
                throw new InvalidOperationException("Ein Kameraauftrag oder eine manuelle Bewegung läuft bereits.");
            }
            if (needsConnection) EnsureConnected();
            exclusiveBusy = true;
            lastError = null;
            return true;
        }
    }

    private void EndExclusive()
    {
        lock (stateLock) exclusiveBusy = false;
        Notify();
    }

    private void EnsureManualAllowed()
    {
        EnsureConnected();
        if (exclusiveBusy || stopping) throw new InvalidOperationException("Ein Kameraauftrag oder Stopp läuft bereits.");
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!connected || controller is null) throw new InvalidOperationException("Die Kamera ist nicht verbunden.");
    }

    private ViscaController GetController()
    {
        lock (stateLock) return controller ?? throw new InvalidOperationException("Die Kamera ist nicht verbunden.");
    }

    private Exception DisconnectForFailure(Exception exception, CancellationToken cancellationToken)
    {
        string message = cancellationToken.IsCancellationRequested ? "Der Kameraauftrag wurde abgebrochen." : "Der Kameraauftrag ist fehlgeschlagen. Verbindung erneut prüfen.";
        DropController(message);
        Notify();
        return cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(message, exception, cancellationToken)
            : new CameraOperationException(message, exception);
    }

    private void DropController(string? error)
    {
        ViscaController? previous;
        lock (stateLock)
        {
            previous = controller;
            controller = null;
            connected = false;
            pumpRunning = stopping = false;
            panSpeed = tiltSpeed = zoomSpeed = 0;
            pendingPan = activePan = null;
            pendingZoom = activeZoom = null;
            lastError = error;
        }
        previous?.Dispose();
    }

    private bool MovingLocked => panSpeed != 0 || tiltSpeed != 0 || zoomSpeed != 0 ||
        pendingPan is { } p && (p.Pan != 0 || p.Tilt != 0) || activePan is { } a && (a.Pan != 0 || a.Tilt != 0) ||
        pendingZoom is { Zoom: not 0 } || activeZoom is { Zoom: not 0 };
    private static bool? Direction(int value) => value == 0 ? null : value > 0;
    private static byte Speed(int value) => (byte)Math.Max(Math.Abs(value), 1);
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync().ConfigureAwait(false); }
        finally { DropController(null); lock (stateLock) disposed = true; }
    }
}
