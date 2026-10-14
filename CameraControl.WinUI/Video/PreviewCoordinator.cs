using System.Runtime.InteropServices;
using System.Text.Json;
using CameraControl.AppCore.Presets;
using Microsoft.UI.Dispatching;

namespace CameraControl_WinUI.Video;

public sealed class PreviewCoordinator : IPositionPhotoSource, IAsyncDisposable
{
    private readonly ApplicationServices services;
    private readonly DispatcherQueue dispatcher;
    private readonly SemaphoreSlim operations = new(1, 1);
    private PreviewWindow? window;
    private CancellationTokenSource? windowLifetime;
    private long connectedRevision = -1;
    private bool disposed;

    private PreviewCoordinator(ApplicationServices services, MainWindow mainWindow)
    {
        this.services = services;
        dispatcher = mainWindow.DispatcherQueue;
        services.ConfigureCredentialStore(new WindowsRtspCredentialStore(services.IsQa, services.RtspCredentialTarget));
        services.ConfigurePreview(this, OpenAsync, DisposeAsync);
    }

    public static PreviewCoordinator Attach(ApplicationServices services, MainWindow mainWindow) => new(services, mainWindow);
    public Task OpenAsync(CancellationToken cancellationToken = default) => RunAsync(() => OpenCoreAsync(cancellationToken));
    public Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    private async Task OpenCoreAsync(CancellationToken cancellationToken, bool force = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operations.WaitAsync(cancellationToken);
        try
        {
            if (!services.CanOperate || services.IsConnectionChanging) throw new InvalidOperationException("Die Vorschau ist noch nicht freigegeben.");
            if (!Uri.TryCreate(services.Settings.Connection.RtspUrl, UriKind.Absolute, out var address) || address.Scheme != "rtsp" || address.UserInfo.Length != 0)
                throw new PhotoCaptureException("Trage zuerst eine vollständige RTSP-URL ohne eingebettete Zugangsdaten ein.");
            services.ValidatePreviewEndpoint(address);
            if (window is null)
            {
                windowLifetime = new CancellationTokenSource();
                var created = new PreviewWindow(services.Settings.Windows.Preview, CloseAsync,
                    () => RunAsync(() => OpenCoreAsync(CancellationToken.None, force: true)),
                    () => services.UpdateVideoStatus("Beim Schließen der Vorschau ist ein Fehler aufgetreten.", true));
                window = created;
                created.StatusChanged += (_, _) =>
                {
                    if (!disposed && ReferenceEquals(window, created))
                    {
                        services.UpdateVideoStatus(created.StatusMessage, created.HasError);
                        WriteQaState();
                    }
                };
            }
            var current = window;
            current.Activate();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, services.LifetimeToken, windowLifetime!.Token);
            await current.WaitForInitializedAsync(cancellation.Token);
            long revision = services.ConnectionRevision;
            if (force || connectedRevision != revision || current.Video.Phase != "playing")
            {
                var credential = await services.ReadRtspCredentialAsync(cancellation.Token);
                if (services.Settings.Connection.CredentialTarget is not null && credential is null)
                    throw new PhotoCaptureException("Für die Verbindung fehlen gespeicherte Zugangsdaten.");
                await current.Video.OpenAsync(address, credential);
                connectedRevision = revision;
            }
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (current.Video.Phase != "playing" || current.Video.DecodedFrames <= 0 || current.Video.DisplayedFrames <= 0)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                current.Video.ObserveFrameState();
                if (current.Video.Phase == "error" || DateTime.UtcNow >= deadline) throw new PhotoCaptureException("Die Vorschau liefert kein decodiertes Bild.");
                await Task.Delay(50, cancellation.Token);
            }
            if (services.ConnectionRevision != revision) throw new PhotoCaptureException("Die Verbindungseinstellungen haben sich geändert. Verbinde die Vorschau erneut.");
            WriteQaState();
        }
        finally { operations.Release(); }
    }

    public async Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken)
    {
        byte[]? result = null;
        await RunAsync(async () =>
        {
            await operations.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var current = window;
                if (current is null || current.IsClosing || connectedRevision != services.ConnectionRevision)
                    throw new PhotoCaptureException("Die Vorschau ist für diese Aufnahme nicht bereit.");
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, services.LifetimeToken, windowLifetime!.Token);
                services.ValidatePreviewEndpoint(new Uri(services.Settings.Connection.RtspUrl!));
                var directory = Path.Combine(Path.GetTempPath(), "Versammlungsptzsteuerung-Photo-QA", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "snapshot.png");
                try
                {
                    await current.Video.TakeSnapshotAsync(path, cancellation.Token);
                    var bytes = await File.ReadAllBytesAsync(path, cancellation.Token);
                    await PngImageDecoder.DecodeAsync(bytes, cancellation.Token);
                    if (connectedRevision != services.ConnectionRevision || current.IsClosing)
                        throw new PhotoCaptureException("Die Vorschau hat sich während der Aufnahme geändert.");
                    result = bytes;
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                    Directory.Delete(directory);
                }
            }
            finally { operations.Release(); }
        });
        return result ?? throw new PhotoCaptureException("Die Aufnahme hat keine PNG-Daten geliefert.");
    }

    private Task CloseAsync() => RunAsync(async () =>
    {
        windowLifetime?.Cancel();
        window?.BeginClose();
        await operations.WaitAsync();
        try
        {
            if (window is not { } current) return;
            var placement = current.CurrentPlacement();
            try
            {
                await current.DisposePlayerAsync();
                await services.SavePreviewPlacementAsync(placement);
            }
            finally
            {
                window = null;
                connectedRevision = -1;
                current.FinishClose();
                windowLifetime?.Dispose();
                windowLifetime = null;
                WriteQaState();
            }
            if (!disposed) services.UpdateVideoStatus("Nicht geöffnet", false);
        }
        finally { operations.Release(); }
    });

    public async ValueTask DisposeAsync()
    {
        await RunAsync(() => { disposed = true; return Task.CompletedTask; });
        await CloseAsync();
    }

    private Task RunAsync(Func<Task> action)
    {
        if (dispatcher.HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new InvalidOperationException("Der Fenster-Dispatcher ist nicht verfügbar."));
        return completion.Task;
    }

    private void WriteQaState()
    {
        if (!services.IsQa || services.IsReadOnly) return;
        var state = new
        {
            Hwnd = window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(window).ToInt64(),
            ActivePlayers = VideoPlayerService.ActivePlayerCount,
            Phase = window?.Video.Phase ?? "closed",
            DecodedFrames = window?.Video.DecodedFrames ?? 0,
            DisplayedFrames = window?.Video.DisplayedFrames ?? 0,
            ConnectionRevision = connectedRevision, Disposed = disposed
        };
        var temporary = Path.Combine(services.DataDirectory, "preview-qa.tmp");
        var destination = Path.Combine(services.DataDirectory, "preview-qa.json");
        Directory.CreateDirectory(services.DataDirectory);
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }
}
