using System.Diagnostics;
using LibVLCSharp.Shared;

namespace CameraControl_WinUI.Video;

public sealed class VideoPlayerService : IAsyncDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private LibVLC? _libvlc;
    private MediaPlayer? _player;
    private Media? _media;
    private volatile bool _stopping;
    private volatile bool _disposed;
    private long _playbackTime;
    private static int _activePlayers;
    public static int ActivePlayerCount => Volatile.Read(ref _activePlayers);

    public event EventHandler? Changed;
    public event Action<string>? Diagnostic;
    public string Phase { get; private set; } = "stopped";
    public string Error { get; private set; } = "";
    public string NativeDirectory { get; private set; } = "";
    public long PlaybackTime => Interlocked.Read(ref _playbackTime);
    public int DecodedFrames { get; private set; }
    public int DisplayedFrames { get; private set; }
    public bool IsInitialized => _player is not null;
    public MediaPlayer? Player => _player;

    public bool PreflightNativeBundle()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoPlayerService));
        try
        {
            NativeDirectory = FindNativeDirectory();
            return true;
        }
        catch (IOException error)
        {
            SetState("error", "Die Videobibliothek kann nicht geladen werden. " + error.Message);
            return false;
        }
    }

    public void Initialize(string[] swapChainOptions)
    {
        if (_player is not null || _disposed || !PreflightNativeBundle()) return;
        try
        {
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", Path.Combine(NativeDirectory, "plugins"));
            Core.Initialize(NativeDirectory);
            _libvlc = new LibVLC(Diagnostic is not null, [.. swapChainOptions, "--no-audio", "--aout=dummy", "--no-osd", "--no-snapshot-preview", "--rtsp-tcp", "--network-caching=150"]);
            _libvlc.Log += OnLog;
            _player = new MediaPlayer(_libvlc);
            Interlocked.Increment(ref _activePlayers);
            _player.TimeChanged += OnTimeChanged;
            _player.EncounteredError += OnPlaybackError;
            _player.EndReached += OnStreamEnded;
            SetState("stopped", "");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or VLCException or DllNotFoundException or BadImageFormatException)
        {
            SetState("error", "Die Videobibliothek kann nicht geladen werden. " + error.Message);
        }
    }

    public async Task OpenAsync(Uri rtspAddress, RtspCredential? credential = null)
    {
        await _operations.WaitAsync();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VideoPlayerService));
            if (_player is null || _libvlc is null)
            {
                SetState("error", string.IsNullOrEmpty(Error) ? "Die Videobibliothek ist nicht bereit." : Error);
                return;
            }
            await StopCoreAsync();
            Interlocked.Exchange(ref _playbackTime, 0);
            DecodedFrames = 0;
            DisplayedFrames = 0;
            _media = new Media(_libvlc, rtspAddress);
            if (credential is not null)
            {
                _media.AddOption(":rtsp-user=" + credential.UserName);
                _media.AddOption(":rtsp-pwd=" + credential.Password);
            }
            SetState("opening", "");
            if (!await Task.Run(() => _player.Play(_media))) SetState("error", "Der Videostream kann nicht gestartet werden.");
        }
        finally { _operations.Release(); }
    }

    public async Task TakeSnapshotAsync(string path, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            if (_player is null || Phase != "playing" || DecodedFrames <= 0 || DisplayedFrames <= 0)
                throw new InvalidOperationException("Für die Aufnahme ist noch kein Videobild verfügbar.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnSnapshot(object? sender, MediaPlayerSnapshotTakenEventArgs args) => completion.TrySetResult();
            _player.SnapshotTaken += OnSnapshot;
            try
            {
                if (!_player.TakeSnapshot(0, path, 0, 0)) throw new IOException("Die Bildaufnahme kann nicht gestartet werden.");
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Die Bilddatei wurde nicht vollständig geschrieben.");
            }
            finally { _player.SnapshotTaken -= OnSnapshot; }
        }
        finally { _operations.Release(); }
    }

    public async Task StopAsync()
    {
        await _operations.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _operations.Release(); }
    }

    private async Task StopCoreAsync()
    {
        _stopping = true;
        try
        {
            if (_player is not null) await Task.Run(_player.Stop);
            _media?.Dispose();
            _media = null;
            Interlocked.Exchange(ref _playbackTime, 0);
            DecodedFrames = 0;
            DisplayedFrames = 0;
            if (_player is not null) SetState("stopped", "");
        }
        finally { _stopping = false; }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args)
    {
        if (_disposed || _stopping) return;
        Interlocked.Exchange(ref _playbackTime, args.Time);
    }

    public void ObserveFrameState()
    {
        if (_disposed || !_operations.Wait(0)) return;
        try
        {
            if (_player is null || _media is null || Phase is not ("opening" or "playing")) return;
            if (_media.Statistics is { } statistics)
            {
                DecodedFrames = statistics.DecodedVideo;
                DisplayedFrames = statistics.DisplayedPictures;
                if (DecodedFrames > 0 && DisplayedFrames > 0 && _player.VoutCount > 0) SetState("playing", "");
            }
        }
        finally { _operations.Release(); }
    }

    private void OnLog(object? sender, LogEventArgs args) => Diagnostic?.Invoke($"{args.Level} {args.Module}: {args.Message}");

    private void OnPlaybackError(object? sender, EventArgs args)
    {
        if (!_disposed && !_stopping) SetState("error", "Der Videostream ist nicht verfügbar. Verbinden Sie die Vorschau erneut.");
    }

    private void OnStreamEnded(object? sender, EventArgs args)
    {
        if (!_disposed && !_stopping) SetState("error", "Der Videostream wurde beendet. Verbinden Sie die Vorschau erneut.");
    }

    private void SetState(string phase, string error)
    {
        Phase = phase;
        Error = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string FindNativeDirectory()
    {
        if (NativeVideoBundle.TryLocate(out var directory)) return directory;
        throw new FileNotFoundException("Die nativen VLC-Dateien oder der Pluginordner fehlen im Programmpaket.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _operations.WaitAsync();
        try
        {
            await VideoShutdown.RunAsync(StopCoreAsync, () =>
            {
                _disposed = true;
                _media?.Dispose();
                _media = null;
            }, () =>
            {
                try
                {
                    if (_player is not null)
                    {
                        _player.TimeChanged -= OnTimeChanged;
                        _player.EncounteredError -= OnPlaybackError;
                        _player.EndReached -= OnStreamEnded;
                        _player.Dispose();
                        _player = null;
                        Interlocked.Decrement(ref _activePlayers);
                    }
                }
                finally
                {
                    if (_libvlc is not null) _libvlc.Log -= OnLog;
                    _libvlc?.Dispose();
                    _libvlc = null;
                }
                return Task.CompletedTask;
            });
        }
        finally { _operations.Release(); }
    }
}
