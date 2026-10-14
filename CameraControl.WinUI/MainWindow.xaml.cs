using System.Runtime.InteropServices;
using CameraControl.AppCore;
using CameraControl_WinUI.ViewModels;
using CameraControl_WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CameraControl_WinUI;

public sealed partial class MainWindow : Window
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ShellViewModel model;
    private readonly PositionsView positions;
    private readonly ControlView control;
    private readonly SettingsView settings;
    private readonly Grid combined = new() { ColumnSpacing = 16 };
    private readonly SemaphoreSlim layoutGate = new(1, 1);
    private long layoutRevision;
    private string? displayedLayout;
    private bool closeAllowed;
    private bool closeRunning;
    private bool adjustingSize;
    private bool photoRunning;
    private CancellationTokenSource? photoCancellation;
    public ApplicationServices Services { get; }

    public MainWindow() : this(ApplicationServices.CreateDevelopment()) { }

    public MainWindow(ApplicationServices services)
    {
        Services = services;
        InitializeComponent();
        if (services.IsQa)
            Root.RequestedTheme = Environment.GetEnvironmentVariable("CAMERA_CONTROL_QA_THEME") switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        model = new ShellViewModel(services, DispatcherQueue);
        positions = new PositionsView(model);
        control = new ControlView(model);
        settings = new SettingsView(model);
        combined.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 344 });
        combined.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 440 });
        AutomationProperties.SetAutomationId(combined, "CombinedControlHost");
        AutomationProperties.SetName(combined, "Positionen und Steuerung");
        control.PhotoRequested += OnPhotoRequested;
        settings.ControlRequested += OnControlRequested;
        PageContent.Content = positions;
        MainNavigation.SelectedItem = MainNavigation.MenuItems[0];
        Root.Loaded += OnLoaded;
        model.Changed += OnModelChanged;
        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnWindowChanged;
        Activated += async (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated) await control.StopInputAsync(); };
        Closed += (_, _) => { model.Changed -= OnModelChanged; model.Dispose(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            await Services.InitializeAsync();
            RestoreWindow();
            await ApplyPageLayoutAsync();
            OnModelChanged(this, EventArgs.Empty);
            DispatcherQueue.TryEnqueue(() => ready.TrySetResult());
        }
        catch (Exception error) { ready.TrySetException(error); model.ShowMessage("Die Einstellungen können nicht geladen werden."); }
    }

    private void OnModelChanged(object? sender, EventArgs args)
    {
        ControlStatus.Text = model.Status;
        ToolTipService.SetToolTip(ControlStatus, model.Status);
        var notice = string.IsNullOrWhiteSpace(model.Message) ? Services.LoadMessage : model.Message;
        if (!string.IsNullOrWhiteSpace(notice) && (model.MessageIsError || Services.NeedsNewSetup)) notice = "Fehler: " + notice;
        OperationMessage.Text = notice ?? string.Empty;
        OperationMessage.Visibility = string.IsNullOrWhiteSpace(notice) ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(OperationMessage, notice);
        OpenPreview.IsEnabled = Services.CanOperate;
    }

    private async void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (model is null || args.SelectedItem is not NavigationViewItem item) return;
        await ApplyPageLayoutAsync(stopForNavigation: true);
    }

    private async void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (model is null || !Services.IsInitialized) return;
        await ApplyPageLayoutAsync();
    }

    private async Task ApplyPageLayoutAsync(bool stopForNavigation = false)
    {
        var revision = ++layoutRevision;
        await layoutGate.WaitAsync();
        try
        {
            if (revision != layoutRevision || closeRunning || Services.IsClosing) return;
            var page = (MainNavigation.SelectedItem as NavigationViewItem)?.Tag as string ?? "positions";
            var next = page == "settings" ? "settings" : Root.ActualWidth >= 800 ? "combined" : page;
            if (next == displayedLayout && !stopForNavigation) return;
            await control.StopInputAsync();
            if (revision != layoutRevision || closeRunning || Services.IsClosing || next == displayedLayout) return;
            PageContent.Content = null;
            combined.Children.Clear();
            if (next == "combined")
            {
                Grid.SetColumn(positions, 0); Grid.SetColumn(control, 1);
                combined.Children.Add(positions); combined.Children.Add(control);
                PageContent.Content = combined;
            }
            else PageContent.Content = next switch { "control" => control, "settings" => settings, _ => positions };
            displayedLayout = next;
        }
        finally { layoutGate.Release(); }
    }

    private async void OnControlRequested(object? sender, EventArgs args)
    {
        MainNavigation.SelectedItem = MainNavigation.MenuItems[1];
        await ApplyPageLayoutAsync(stopForNavigation: true);
    }

    private async void OnPhotoRequested(object? sender, EventArgs args)
    {
        if (photoRunning || closeRunning || Services.IsClosing) return;
        photoRunning = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Services.LifetimeToken);
        photoCancellation = cancellation;
        try
        {
            await control.StopInputAsync();
            await model.SaveSelectedPhotoAsync(ConfirmPhotoAsync, cancellation.Token);
        }
        finally
        {
            photoCancellation = null;
            photoRunning = false;
        }
    }

    private async Task<bool> ConfirmPhotoAsync(CameraControl.AppCore.Presets.PhotoConfirmation photo,
        CancellationToken cancellationToken)
    {
        var confirmed = false;
        await OnDispatcherAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (closeRunning || Services.IsClosing) return;
            Activate();
            confirmed = await PhotoConfirmationDialog.ShowAsync(Root.XamlRoot, photo, cancellationToken);
        });
        return confirmed;
    }

    private async void OnPreview(object sender, RoutedEventArgs args) => await model.RunAsync(() => Services.OpenPreviewAsync());

    public Task WaitForResponsiveReadyAsync(CancellationToken cancellationToken = default) => ready.Task.WaitAsync(cancellationToken);

    public Task ActivateForDeploymentAsync(CancellationToken cancellationToken = default) => OnDispatcherAsync(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Activate();
        return Task.CompletedTask;
    });

    public async Task<bool> CloseForDeploymentAsync(CancellationToken cancellationToken = default)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try { completion.TrySetResult(await CloseForDeploymentAsync(cancellationToken)); }
                catch (Exception error) { completion.TrySetException(error); }
            })) return false;
            return await completion.Task.WaitAsync(cancellationToken);
        }
        if (closeAllowed) return true;
        if (closeRunning) return false;
        closeRunning = true;
        photoCancellation?.Cancel();
        positions.SuspendRendering();
        settings.SuspendImages();
        try
        {
            await control.StopInputAsync();
            MainNavigation.IsEnabled = false;
            if (Services.CanOperate) await Services.SaveMainPlacementAsync(CurrentPlacement(), cancellationToken);
            await Services.CloseAsync(cancellationToken);
            closeAllowed = true;
            Close();
            return true;
        }
        catch (Exception error) { model.ShowMessage("Schließen fehlgeschlagen: " + error.Message); return false; }
        finally
        {
            closeRunning = false;
            if (!closeAllowed)
            {
                MainNavigation.IsEnabled = true;
                if (positions.IsLoaded) positions.ResumeRendering();
                if (settings.IsLoaded) settings.ResumeImages();
            }
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeAllowed) return;
        args.Cancel = true;
        await CloseForDeploymentAsync();
    }

    private Task OnDispatcherAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new InvalidOperationException("Der Fenster-Dispatcher ist nicht verfügbar."));
        return completion.Task;
    }

    private void RestoreWindow()
    {
        var saved = Services.Settings.Windows.Main;
        var display = saved is null ? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromPoint(new PointInt32((int)saved.X, (int)saved.Y), DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        var restored = WindowLayoutPolicy.Restore(saved, Scale, new PixelBounds(area.X, area.Y, area.Width, area.Height));
        AppWindow.MoveAndResize(new RectInt32(restored.X, restored.Y, restored.Width, restored.Height));
    }

    private double Scale => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
    private WindowPlacement CurrentPlacement() => new(AppWindow.Position.X, AppWindow.Position.Y,
        AppWindow.Size.Width, AppWindow.Size.Height, Scale);

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || adjustingSize || !Services.IsInitialized) return;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min(area.Width, Math.Max(AppWindow.Size.Width, (int)Math.Ceiling(360 * Scale)));
        var height = Math.Min(area.Height, Math.Max(AppWindow.Size.Height, (int)Math.Ceiling(280 * Scale)));
        if (width == AppWindow.Size.Width && height == AppWindow.Size.Height) return;
        adjustingSize = true;
        try { AppWindow.Resize(new SizeInt32(width, height)); }
        finally { adjustingSize = false; }
    }

    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(nint window);
}
