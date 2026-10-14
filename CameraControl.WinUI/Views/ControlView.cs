using CameraControl.AppCore;
using CameraControl.AppCore.Control;
using CameraControl_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CameraControl_WinUI.Views;

public sealed class ControlView : UserControl
{
    public event EventHandler? PhotoRequested;
    private readonly ShellViewModel model;
    private readonly Button pan = new() { Width = 200, Height = 200, Content = new TextBlock
        { Text = "↑\n←       →\n↓", FontSize = 20, TextAlignment = TextAlignment.Center } };
    private readonly Button zoom = new() { Width = 200, Height = 44, Content = "−       Zoom       +" };
    private readonly Button stop = new() { Content = "Stopp" };
    private readonly Button home = new() { Content = "Home" };
    private readonly TextBlock speeds = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly ComboBox selectedPreset = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button recallPosition = new() { Content = "Aufrufen" };
    private readonly Button capturePhoto = new() { Content = "Foto" };
    private readonly TextBlock saveHint = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
        Visibility = Visibility.Collapsed };
    private readonly Grid layout = new() { Margin = new Thickness(8), RowSpacing = 8 };
    private readonly Grid fields = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly StackPanel panel = new() { Spacing = 4 };
    private readonly StackPanel right = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel commands = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid commandDock = new();
    private readonly TextBlock keyboardHelp = new() { Text = "Tastatur: Pfeiltasten im Steuerfeld. Taste loslassen sendet Stopp.",
        TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private bool? wideLayout;
    private bool refreshingSelection;
    private readonly SemaphoreSlim layoutGate = new(1, 1);
    private long layoutRevision;
    private readonly HashSet<VirtualKey> keys = [];
    private uint? panPointer;
    private uint? zoomPointer;
    private Task? observedMotion;
    private Task motionObserver = Task.CompletedTask;

    public ControlView(ShellViewModel model)
    {
        this.model = model;
        var selection = new Grid { ColumnSpacing = 8 };
        selection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selection.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selection.Children.Add(new TextBlock { Text = "Ansicht", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(selectedPreset, 1); selection.Children.Add(selectedPreset);
        Grid.SetColumn(recallPosition, 2); selection.Children.Add(recallPosition);
        panel.Children.Add(selection);
        var positionActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        capturePhoto.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        positionActions.Children.Add(capturePhoto);
        panel.Children.Add(positionActions); panel.Children.Add(saveHint);
        fields.Children.Add(pan);
        fields.Children.Add(right);
        right.Children.Add(zoom);
        right.Children.Add(speeds);
        commands.Children.Add(stop);
        commands.Children.Add(home);
        panel.Children.Add(fields);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(scroll, "ControlScroll");
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(commandDock, 1);
        layout.Children.Add(scroll);
        layout.Children.Add(commandDock);
        Content = layout;
        SizeChanged += async (_, _) => await ApplyLayoutAsync();
        ApplyLayoutCore(false);
        AutomationProperties.SetAutomationId(this, "ControlPage");
        SetIdentity(pan, "PanTiltPad", "Pan- und Tilt-Steuerfeld");
        SetIdentity(zoom, "ZoomPad", "Zoom-Steuerfeld");
        SetIdentity(stop, "StopMotion", "Bewegung stoppen");
        SetIdentity(home, "ControlHome", "Home aufrufen");
        AutomationProperties.SetAutomationId(speeds, "MotionSpeeds");
        SetIdentity(selectedPreset, "ControlPresetSelector", "Gewählte Position");
        SetIdentity(recallPosition, "RecallSelectedPosition", "Gewählte Position aufrufen");
        SetIdentity(capturePhoto, "CaptureSelectedPhoto", "Foto für die gewählte Ansicht aufnehmen");
        AutomationProperties.SetAutomationId(saveHint, "CaptureSelectedPhotoHint");
        ToolTipService.SetToolTip(pan, "Zeiger vom Mittelpunkt weg bewegen. Loslassen sendet Stopp.");
        selectedPreset.SelectionChanged += (_, _) =>
        {
            if (!refreshingSelection && selectedPreset.SelectedIndex >= 0)
                model.SelectPreset(PresetIds.Ordered[selectedPreset.SelectedIndex]);
        };
        recallPosition.Click += async (_, _) => await model.RecallSelectedAsync();
        capturePhoto.Click += (_, _) => PhotoRequested?.Invoke(this, EventArgs.Empty);
        AddPointerHandlers(pan, OnPanPressed, OnPanMoved);
        AddPointerHandlers(zoom, OnZoomPressed, OnZoomMoved);
        pan.AddHandler(KeyDownEvent, new KeyEventHandler(OnPanKeyDown), true);
        pan.AddHandler(KeyUpEvent, new KeyEventHandler(OnPanKeyUp), true);
        zoom.AddHandler(KeyDownEvent, new KeyEventHandler(OnZoomKeyDown), true);
        zoom.AddHandler(KeyUpEvent, new KeyEventHandler(OnZoomKeyUp), true);
        pan.LostFocus += async (_, _) => await StopInputAsync();
        zoom.LostFocus += async (_, _) => await StopInputAsync();
        stop.Click += async (_, _) => await StopInputAsync();
        home.Click += async (_, _) => await model.RecallAsync(model.Services.Settings.Presets.Single(preset => preset.Id == PresetIds.Home));
        model.Changed += OnChanged;
        Loaded += (_, _) => Refresh();
        Unloaded += async (_, _) => await StopInputAsync();
        Refresh();
    }

    private async Task ApplyLayoutAsync()
    {
        layout.Margin = new Thickness(8);
        var wide = ActualWidth >= 440 && ActualHeight >= 260;
        if (wideLayout == wide && layoutGate.CurrentCount == 1) return;
        var revision = ++layoutRevision;
        await layoutGate.WaitAsync();
        try
        {
            if (revision != layoutRevision || model.Services.IsClosing) return;
            await StopInputAsync();
            if (revision != layoutRevision || model.Services.IsClosing) return;
            ApplyLayoutCore(ActualWidth >= 440 && ActualHeight >= 260);
        }
        finally { layoutGate.Release(); }
    }

    private void ApplyLayoutCore(bool wide)
    {
        wideLayout = wide;
        fields.RowDefinitions.Clear();
        fields.ColumnDefinitions.Clear();
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = wide ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        if (wide) fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        else fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(right, wide ? 0 : 1);
        Grid.SetColumn(right, wide ? 1 : 0);
        right.Children.Remove(commands);
        right.Children.Remove(keyboardHelp);
        panel.Children.Remove(keyboardHelp);
        commandDock.Children.Clear();
        if (wide)
        {
            right.Children.Add(commands);
            right.Children.Add(keyboardHelp);
        }
        else
        {
            commandDock.Children.Add(commands);
            panel.Children.Add(keyboardHelp);
        }
        commandDock.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddPointerHandlers(Button button, PointerEventHandler pressed, PointerEventHandler moved)
    {
        button.AddHandler(PointerPressedEvent, pressed, true);
        button.AddHandler(PointerMovedEvent, moved, true);
        button.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        button.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        button.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerLost), true);
    }

    private void OnPanPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!model.CanControl || !args.GetCurrentPoint(pan).Properties.IsLeftButtonPressed || !pan.CapturePointer(args.Pointer)) return;
        panPointer = args.Pointer.PointerId;
        args.Handled = true;
        QueuePan(args);
    }

    private void OnPanMoved(object sender, PointerRoutedEventArgs args)
    {
        if (panPointer == args.Pointer.PointerId && model.CanControl) QueuePan(args);
    }

    private void QueuePan(PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(pan).Position;
        var speed = ManualControlMap.PanTilt(point.X, point.Y, pan.ActualWidth, pan.ActualHeight);
        Observe(() => model.Services.SetPanTiltAsync(speed.Pan, speed.Tilt));
    }

    private void OnZoomPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!model.CanControl || !args.GetCurrentPoint(zoom).Properties.IsLeftButtonPressed || !zoom.CapturePointer(args.Pointer)) return;
        zoomPointer = args.Pointer.PointerId;
        args.Handled = true;
        QueueZoom(args);
    }

    private void OnZoomMoved(object sender, PointerRoutedEventArgs args)
    {
        if (zoomPointer == args.Pointer.PointerId && model.CanControl) QueueZoom(args);
    }

    private void QueueZoom(PointerRoutedEventArgs args) => Observe(() => model.Services.SetZoomAsync(
        ManualControlMap.Zoom(args.GetCurrentPoint(zoom).Position.X, zoom.ActualWidth)));

    private async void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (panPointer != args.Pointer.PointerId && zoomPointer != args.Pointer.PointerId) return;
        args.Handled = true;
        await StopInputAsync();
    }

    private async void OnPointerLost(object sender, PointerRoutedEventArgs args)
    {
        if (panPointer == args.Pointer.PointerId || zoomPointer == args.Pointer.PointerId) await StopInputAsync();
    }

    private void OnPanKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!model.CanControl || !IsDirection(args.Key)) return;
        args.Handled = true;
        keys.Add(args.Key);
        QueueKeys();
    }

    private void OnPanKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (!IsDirection(args.Key)) return;
        args.Handled = true;
        keys.Remove(args.Key);
        if (model.CanControl) QueueKeys();
    }

    private void QueueKeys() => Observe(() => model.Services.SetPanTiltAsync(
        (keys.Contains(VirtualKey.Right) ? 24 : 0) - (keys.Contains(VirtualKey.Left) ? 24 : 0),
        (keys.Contains(VirtualKey.Up) ? 24 : 0) - (keys.Contains(VirtualKey.Down) ? 24 : 0)));

    private void OnZoomKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!model.CanControl || args.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        args.Handled = true;
        Observe(() => model.Services.SetZoomAsync(args.Key == VirtualKey.Left ? -7 : 7));
    }

    private void OnZoomKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        args.Handled = true;
        if (model.CanControl) Observe(() => model.Services.SetZoomAsync(0));
    }

    private void Observe(Func<Task> operation)
    {
        try
        {
            var task = operation();
            if (ReferenceEquals(task, observedMotion)) return;
            observedMotion = task;
            motionObserver = model.RunAsync(() => task);
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        { model.ShowMessage(error.Message); }
    }

    public async Task StopInputAsync()
    {
        panPointer = zoomPointer = null;
        keys.Clear();
        pan.ReleasePointerCaptures();
        zoom.ReleasePointerCaptures();
        await model.StopAsync();
        await motionObserver;
    }

    private static bool IsDirection(VirtualKey key) => key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right;
    private void OnChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh()
    {
        pan.IsEnabled = zoom.IsEnabled = model.CanControl;
        stop.IsEnabled = model.Services.Camera.IsConnected && model.Services.Camera.IsMoving;
        home.IsEnabled = model.CanControl && !model.Services.Camera.IsMoving;
        speeds.Text = $"Pan: {model.Services.Camera.PanSpeed} · Tilt: {model.Services.Camera.TiltSpeed} · Zoom: {model.Services.Camera.ZoomSpeed}";
        refreshingSelection = true;
        try
        {
            var names = PresetIds.Ordered.Select(id => id == model.SelectedPresetId ? model.SelectedPresetName :
                model.Services.Settings.Presets.Single(value => value.Id == id).Name).ToArray();
            if (!selectedPreset.Items.Cast<string>().SequenceEqual(names))
            {
                selectedPreset.Items.Clear(); foreach (var name in names) selectedPreset.Items.Add(name);
            }
            selectedPreset.SelectedIndex = PresetIds.Ordered.ToList().IndexOf(model.SelectedPresetId!);
        }
        finally { refreshingSelection = false; }
        selectedPreset.IsEnabled = model.CanSelectPreset;
        var selected = model.Services.Settings.Presets.FirstOrDefault(value => value.Id == model.SelectedPresetId);
        recallPosition.IsEnabled = selected is not null && model.CanRecall(selected);
        capturePhoto.IsEnabled = model.CanTakeSelectedPhoto;
        var reason = model.CanTakeSelectedPhotoReason;
        saveHint.Text = reason ?? "";
        saveHint.Visibility = string.IsNullOrEmpty(reason) || !model.Services.Camera.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(capturePhoto, reason ?? "Foto aufnehmen und für diese Ansicht speichern.");
        ToolTipService.SetToolTip(saveHint, reason);
    }

    private static void SetIdentity(DependencyObject element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }
}
