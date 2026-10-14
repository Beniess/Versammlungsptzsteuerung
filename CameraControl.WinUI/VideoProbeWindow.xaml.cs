using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Concurrent;
using CameraControl_WinUI.Video;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Foundation;

namespace CameraControl_WinUI;

public sealed partial class VideoProbeWindow : Window
{
    private readonly VideoProbeOptions _options;
    private readonly VideoPlayerService _video = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private bool _initialized;
    private bool _busy;
    private bool _closing;
    private int _lastCommandId;
    private string _commandError = "";
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private VideoView? _videoView;

    public VideoProbeWindow(VideoProbeOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.OutputDirectory);
        File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "Probe constructor\n");
        InitializeComponent();
        File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "Probe XAML initialized\n");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 560));
        _video.Changed += OnVideoChanged;
        _video.Diagnostic += OnDiagnostic;
        File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "Native preflight\n");
        if (_video.PreflightNativeBundle())
        {
            File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "Native files verified; before VideoView\n");
            _videoView = new VideoView();
            File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "VideoView constructed\n");
            _videoView.Initialized += OnVideoViewInitialized;
            VideoHost.Children.Add(_videoView);
        }
        UpdateStatus();
        _timer.Tick += OnTick;
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
        _timer.Start();
        File.AppendAllText(Path.Combine(options.OutputDirectory, "startup-stage.txt"), "Probe timer started\n");
    }

    private void OnVideoViewInitialized(object? sender, InitializedEventArgs args)
    {
        File.AppendAllText(Path.Combine(_options.OutputDirectory, "startup-stage.txt"), "VideoView initialized; before VLC\n");
        _initialized = true;
        _video.Initialize(args.SwapChainOptions);
        File.AppendAllText(Path.Combine(_options.OutputDirectory, "startup-stage.txt"), "VLC initialization returned\n");
        _videoView!.MediaPlayer = _video.Player;
        UpdateStatus();
        WriteState();
    }

    private void OnVideoChanged(object? sender, EventArgs args)
    {
        if (_closing) return;
        DispatcherQueue.TryEnqueue(() => { if (!_closing) { UpdateStatus(); WriteState(); } });
    }

    private void OnDiagnostic(string message) => _diagnostics.Enqueue(message);

    private void FlushDiagnostics()
    {
        var lines = new List<string>();
        while (_diagnostics.TryDequeue(out var line)) lines.Add(line);
        if (lines.Count > 0) File.AppendAllLines(Path.Combine(_options.OutputDirectory, "libvlc.log"), lines);
    }

    private void UpdateStatus()
    {
        var error = string.IsNullOrEmpty(_commandError) ? _video.Error : _commandError;
        Status.Severity = string.IsNullOrEmpty(error) ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        Status.Message = !string.IsNullOrEmpty(error) ? error : _video.Phase switch
        {
            "playing" => "Das Livebild ist verfügbar.",
            "opening" => "Die Vorschau wird verbunden.",
            _ => "Vorschau bereit. Der Stream ist geschlossen."
        };
    }

    private async void OnTick(object? sender, object args)
    {
        FlushDiagnostics();
        if (_closing || _busy) return;
        _video.ObserveFrameState();
        var path = Path.Combine(_options.OutputDirectory, "command.json");
        if (!File.Exists(path)) { WriteState(); return; }
        ProbeCommand? command;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            command = JsonSerializer.Deserialize<ProbeCommand>(reader.ReadToEnd(), _json);
        }
        catch (IOException) { return; }
        catch (JsonException error) { _commandError = "Ungültiger Prüfbefehl: " + error.Message; UpdateStatus(); WriteState(); return; }
        if (command is null || command.Id <= _lastCommandId) { WriteState(); return; }
        _busy = true;
        _commandError = "";
        WriteState();
        try
        {
            switch (command.Action)
            {
                case "open": await _video.OpenAsync(_options.RtspAddress); break;
                case "snapshot": await _video.TakeSnapshotAsync(ValidateSnapshotPath(command.Path)); break;
                case "close": await _video.StopAsync(); break;
                case "exit": await _video.DisposeAsync(); break;
                default: throw new ArgumentException("Der Prüfbefehl ist nicht unterstützt.");
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or TimeoutException)
        { _commandError = error.Message; }
        finally
        {
            _lastCommandId = command.Id;
            _busy = false;
            UpdateStatus();
            WriteState();
        }
        if (command.Action == "exit") { _closing = true; _timer.Stop(); Close(); }
    }

    private string ValidateSnapshotPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = _options.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(fullPath) != ".png")
            throw new ArgumentException("Die Prüfaufnahme muss als PNG im eigenen Ausgabeordner liegen.");
        return fullPath;
    }

    private void WriteState()
    {
        if (_closing) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var origin = new NativePoint();
        ClientToScreen(hwnd, ref origin);
        var offset = VideoHost.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var state = new
        {
            Hwnd = hwnd.ToInt64(), Initialized = _initialized, Busy = _busy, LastCommandId = _lastCommandId,
            Phase = string.IsNullOrEmpty(_commandError) ? _video.Phase : "error",
            Error = string.IsNullOrEmpty(_commandError) ? _video.Error : _commandError,
            _video.PlaybackTime, _video.NativeDirectory, _video.DecodedFrames, _video.DisplayedFrames,
            VideoBounds = new { X = origin.X + offset.X * scale,
                Y = origin.Y + offset.Y * scale,
                Width = VideoHost.ActualWidth * scale, Height = VideoHost.ActualHeight * scale }
        };
        var temporary = Path.Combine(_options.OutputDirectory, "state.tmp");
        var destination = Path.Combine(_options.OutputDirectory, "state.json");
        var step = "write-temporary";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, _json));
            if (File.Exists(destination))
            {
                step = "replace-state";
                File.Replace(temporary, destination, null);
            }
            else
            {
                step = "create-state";
                File.Move(temporary, destination);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            File.WriteAllText(Path.Combine(_options.OutputDirectory, "state-write-error.json"),
                JsonSerializer.Serialize(new { Step = step, error.HResult, Message = error.Message, Utc = DateTime.UtcNow }, _json));
            throw;
        }
    }

    private async void OnOpen(object sender, RoutedEventArgs args) => await _video.OpenAsync(_options.RtspAddress);
    private async void OnStop(object sender, RoutedEventArgs args) => await _video.StopAsync();
    private async void OnSnapshot(object sender, RoutedEventArgs args)
    {
        try { await _video.TakeSnapshotAsync(Path.Combine(_options.OutputDirectory, "manual-snapshot.png")); }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
        { _commandError = error.Message; UpdateStatus(); }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) return;
        File.WriteAllText(Path.Combine(_options.OutputDirectory, "window-closing.json"),
            JsonSerializer.Serialize(new { Utc = DateTime.UtcNow, LastCommandId = _lastCommandId, Busy = _busy }, _json));
        args.Cancel = true;
        _timer.Stop();
        await _video.DisposeAsync();
        _closing = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _timer.Stop();
        _video.Changed -= OnVideoChanged;
        _video.Diagnostic -= OnDiagnostic;
        FlushDiagnostics();
    }

    private sealed record ProbeCommand(int Id, string Action, string Path);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
}
