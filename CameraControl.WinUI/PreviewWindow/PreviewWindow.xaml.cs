using System.Runtime.InteropServices;
using CameraControl.AppCore;
using CameraControl.AppCore.Presets;
using CameraControl_WinUI.Video;
using CameraControl_WinUI.Views;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CameraControl_WinUI;

public sealed partial class PreviewWindow : Window
{
    private readonly VideoPlayerService video = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<Task> closeRequested;
    private readonly Func<Task> reconnect;
    private readonly Action closeError;
    private readonly VideoView? view;
    private bool closing;
    private bool closeAllowed;
    public event EventHandler? StatusChanged;
    public VideoPlayerService Video => video;
    public bool IsClosing => closing;
    public string StatusMessage => Status.Message;
    public bool HasError => Status.Severity == InfoBarSeverity.Error;

    public PreviewWindow(WindowPlacement? placement, Func<Task> closeRequested, Func<Task> reconnect, Action closeError)
    {
        this.closeRequested = closeRequested;
        this.reconnect = reconnect;
        this.closeError = closeError;
        InitializeComponent();
        Root.Loaded += (_, _) => RestorePlacement(placement);
        video.Changed += OnVideoChanged;
        if (video.PreflightNativeBundle())
        {
            view = new VideoView();
            view.Initialized += OnInitialized;
            VideoHost.Children.Add(view);
        }
        else initialized.TrySetException(new PhotoCaptureException("Die Videobibliothek ist nicht verfügbar."));
        timer.Tick += OnTick;
        timer.Start();
        AppWindow.Closing += OnClosing;
    }

    private void OnInitialized(object? sender, InitializedEventArgs args)
    {
        if (closing) return;
        video.Initialize(args.SwapChainOptions);
        view!.MediaPlayer = video.Player;
        if (video.IsInitialized) initialized.TrySetResult();
        else initialized.TrySetException(new PhotoCaptureException("Die Videobibliothek ist nicht verfügbar."));
        UpdateStatus();
    }

    public Task WaitForInitializedAsync(CancellationToken cancellationToken) => initialized.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    private void OnVideoChanged(object? sender, EventArgs args)
    {
        if (closing) return;
        DispatcherQueue.TryEnqueue(() => { if (!closing) UpdateStatus(); });
    }

    private void OnTick(object? sender, object args)
    {
        if (closing) return;
        video.ObserveFrameState();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        Status.Severity = video.Phase == "error" ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Status.Message = video.Phase switch
        {
            "playing" => "Das Livebild ist verfügbar. Ton ist ausgeschaltet.",
            "opening" => "Die Vorschau wird verbunden.",
            "error" => "Die Vorschau ist nicht verfügbar. Prüfe die Verbindung und verbinde erneut.",
            _ => "Die Vorschau ist bereit. Der Stream ist geschlossen."
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnReconnect(object sender, RoutedEventArgs args)
    {
        try { await reconnect(); }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException or TimeoutException or PhotoCaptureException)
        { if (!closing) { Status.Severity = InfoBarSeverity.Error; Status.Message = "Die Vorschau konnte nicht verbunden werden."; } }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeAllowed) return;
        args.Cancel = true;
        try { await closeRequested(); }
        catch (Exception) { closeError(); }
    }

    public WindowPlacement CurrentPlacement() => new(AppWindow.Position.X, AppWindow.Position.Y,
        AppWindow.Size.Width, AppWindow.Size.Height, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0);

    public void BeginClose()
    {
        closing = true;
        timer.Stop();
        video.Changed -= OnVideoChanged;
    }

    public async Task DisposePlayerAsync()
    {
        await VideoShutdown.RunAsync(video.StopAsync,
            () => { if (view is not null) view.MediaPlayer = null; },
            () => video.DisposeAsync().AsTask());
    }

    public void FinishClose()
    {
        closeAllowed = true;
        StatusChanged = null;
        Close();
    }

    private void RestorePlacement(WindowPlacement? saved)
    {
        var display = saved is null ? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromPoint(new PointInt32((int)saved.X, (int)saved.Y), DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var restored = WindowLayoutPolicy.Restore(saved, scale, new PixelBounds(area.X, area.Y, area.Width, area.Height));
        AppWindow.MoveAndResize(new RectInt32(restored.X, restored.Y, restored.Width, restored.Height));
    }

    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(nint hwnd);
}
