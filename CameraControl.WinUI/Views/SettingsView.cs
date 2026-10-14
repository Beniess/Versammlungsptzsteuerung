using CameraControl.AppCore;
using CameraControl_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CameraControl_WinUI.Views;

public sealed class SettingsView : UserControl
{
    public event EventHandler? ControlRequested;
    private readonly ShellViewModel shell;
    private readonly SettingsViewModel model;
    private readonly TextBox host = new();
    private readonly NumberBox port = new() { Minimum = 1, Maximum = 65535,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
    private readonly TextBox rtsp = new() { PlaceholderText = "rtsp://host:port/pfad" };
    private readonly TextBox user = new() { Header = "Benutzername" };
    private readonly PasswordBox password = new() { Header = "Passwort" };
    private readonly CheckBox removeCredential = new() { Content = "Gespeicherte Zugangsdaten entfernen" };
    private readonly ComboBox preset = new() { Header = "Ansicht", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox name = new();
    private readonly Button savePreset = new() { Content = "Name speichern" };
    private readonly Button controlPhoto = new() { Content = "Foto in der Steuerung aufnehmen" };
    private readonly Button reset = new() { Content = "Neu einrichten" };
    private readonly Button licenses = new() { Content = "Version und Lizenzen" };
    private readonly Button saveConnection = new() { Content = "Speichern und verbinden" };
    private readonly Button checkConnection = new() { Content = "Verbindung prüfen" };
    private readonly Expander advanced = new() { Header = "Weitere Optionen",
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool rendering;
    private bool loaded;
    private bool licensesOpen;
    private string? credentialReference;

    public SettingsView(ShellViewModel shell)
    {
        this.shell = shell;
        model = new SettingsViewModel(shell);
        var connection = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        var connectionFields = new Grid { ColumnSpacing = 8 };
        connectionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        connectionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        connectionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        connectionFields.Children.Add(Label("Host/IP", host));
        Grid.SetColumn(host, 1); connectionFields.Children.Add(host);
        var portLabel = Label("Port", port); Grid.SetColumn(portLabel, 2); connectionFields.Children.Add(portLabel);
        Grid.SetColumn(port, 3); connectionFields.Children.Add(port);
        var live = new Grid { ColumnSpacing = 8 };
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        live.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        live.Children.Add(Label("Livebild", rtsp)); Grid.SetColumn(rtsp, 1); live.Children.Add(rtsp);
        ToolTipService.SetToolTip(rtsp, "Vollständige Livebild-Adresse: rtsp://host:port/pfad");
        connection.Children.Add(connectionFields); connection.Children.Add(live);
        saveConnection.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        connection.Children.Add(saveConnection);
        var credentialFields = new StackPanel { Spacing = 4 };
        credentialFields.Children.Add(user); credentialFields.Children.Add(password);
        credentialFields.Children.Add(new TextBlock { Text = "Leeres Passwort erhält gespeicherte Zugangsdaten.",
            FontSize = 14, TextWrapping = TextWrapping.Wrap });
        credentialFields.Children.Add(removeCredential);
        var credentials = new Expander { Header = "Zugangsdaten (optional)", Content = credentialFields,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        SetId(credentials, "CredentialGroup"); connection.Children.Add(credentials);
        var more = new StackPanel { Spacing = 4 };
        more.Children.Add(checkConnection); more.Children.Add(licenses); more.Children.Add(reset);
        advanced.Content = more; SetId(advanced, "AdvancedSettingsGroup"); connection.Children.Add(advanced);
        var positions = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        positions.Children.Add(preset);
        var rename = new Grid { ColumnSpacing = 8 };
        rename.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        rename.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rename.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rename.Children.Add(Label("Name", name)); Grid.SetColumn(name, 1); rename.Children.Add(name);
        Grid.SetColumn(savePreset, 2); rename.Children.Add(savePreset);
        savePreset.VerticalAlignment = VerticalAlignment.Bottom;
        positions.Children.Add(rename);
        positions.Children.Add(new TextBlock { Text = "Foto und Kameraposition direkt in der Steuerung speichern.",
            FontSize = 14, TextWrapping = TextWrapping.Wrap });
        positions.Children.Add(controlPhoto);
        var tabs = new Pivot();
        var connectionTab = new PivotItem { Header = Header("Verbindung"), Content = Scroll(connection, "SettingsScroll") };
        var positionsTab = new PivotItem { Header = Header("Ansichten"), Content = Scroll(positions, "SettingsPositionsScroll") };
        SetId(connectionTab, "SettingsConnectionSection"); SetId(positionsTab, "SettingsPositionsSection");
        tabs.Items.Add(connectionTab); tabs.Items.Add(positionsTab); Content = tabs;
        SetId(this, "SettingsPage");
        SetId(host, "ConnectionHost"); SetId(port, "ConnectionPort"); SetId(rtsp, "ConnectionRtsp");
        SetId(user, "CredentialUserName"); SetId(password, "CredentialPassword"); SetId(removeCredential, "CredentialRemove");
        SetId(preset, "PresetSelector"); SetId(name, "PresetName"); SetId(savePreset, "SavePreset");
        SetId(controlPhoto, "OpenControllerForPhoto"); SetId(reset, "ResetSettings"); SetId(licenses, "OpenLicenses");
        SetId(saveConnection, "ConnectionSave"); SetId(checkConnection, "ConnectionCheck");
        host.TextChanged += OnConnectionEdited; rtsp.TextChanged += OnConnectionEdited;
        port.ValueChanged += (_, _) => OnConnectionEdited(this, null!);
        user.TextChanged += OnCredentialEdited;
        password.PasswordChanged += (_, _) => OnCredentialEdited(this, null!);
        removeCredential.Checked += (_, _) => OnCredentialEdited(this, null!);
        removeCredential.Unchecked += (_, _) => OnCredentialEdited(this, null!);
        name.TextChanged += (_, _) => { if (!rendering) model.SetName(name.Text); };
        preset.SelectionChanged += (_, _) =>
        {
            if (!rendering && preset.SelectedIndex >= 0) model.Select(PresetIds.Ordered[preset.SelectedIndex]);
        };
        saveConnection.Click += async (_, _) =>
        {
            model.SetConnection(host.Text, port.Value, rtsp.Text);
            await model.SaveAndConnectAsync();
            if (!model.ConnectionDirty) { rendering = true; password.Password = ""; rendering = false; }
        };
        checkConnection.Click += async (_, _) =>
        {
            model.SetConnection(host.Text, port.Value, rtsp.Text);
            await model.CheckConnectionAsync();
        };
        savePreset.Click += async (_, _) => await model.SavePresetAsync();
        controlPhoto.Click += (_, _) => ControlRequested?.Invoke(this, EventArgs.Empty);
        reset.Click += async (_, _) => await model.ResetAsync();
        licenses.Click += async (_, _) =>
        {
            if (licensesOpen) return;
            licensesOpen = true;
            try { await shell.RunAsync(() => LicenseView.ShowAsync(XamlRoot)); }
            finally { licensesOpen = false; }
        };
        model.Changed += OnViewModelChanged;
        foreach (var element in EditableControls()) element.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            loaded = true; shell.Changed -= OnShellChanged; shell.Changed += OnShellChanged;
            model.Refresh(); await LoadCredentialAsync();
        };
        Unloaded += (_, _) => SuspendImages();
    }

    private IEnumerable<Control> EditableControls() => [host, port, rtsp, user, password, removeCredential, preset, name,
        checkConnection, saveConnection, savePreset, controlPhoto, reset];

    private static TextBlock Header(string text) => new() { Text = text, FontSize = 14 };
    private static TextBlock Label(string text, Control field)
    {
        var label = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLabeledBy(field, label);
        return label;
    }
    private static ScrollViewer Scroll(UIElement content, string id)
    {
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetId(scroll, id); return scroll;
    }

    private void OnConnectionEdited(object sender, TextChangedEventArgs args)
    {
        if (!rendering) model.SetConnection(host.Text, port.Value, rtsp.Text);
    }

    private void OnCredentialEdited(object sender, TextChangedEventArgs args)
    {
        if (!rendering) model.SetCredential(user.Text, password.Password, removeCredential.IsChecked == true);
    }

    private async void OnShellChanged(object? sender, EventArgs args)
    {
        if (!loaded || shell.Services.IsClosing) return;
        model.Refresh(); await LoadCredentialAsync();
    }

    private void OnViewModelChanged(object? sender, EventArgs args)
    {
        if (!loaded || !shell.Services.IsInitialized || shell.Services.IsClosing) return;
        rendering = true;
        try
        {
            host.Text = model.Host; port.Value = model.Port; rtsp.Text = model.Rtsp;
            user.Text = model.UserName; removeCredential.IsChecked = model.RemoveCredentials;
            var names = PresetIds.Ordered.Select(id => shell.Services.Settings.Presets.Single(value => value.Id == id).Name).ToArray();
            if (!preset.Items.Cast<string>().SequenceEqual(names))
            { preset.Items.Clear(); foreach (var value in names) preset.Items.Add(value); }
            preset.SelectedIndex = PresetIds.Ordered.ToList().IndexOf(model.SelectedId);
            name.Text = model.Name;
            var canEdit = model.CanEdit;
            host.IsEnabled = port.IsEnabled = rtsp.IsEnabled = canEdit;
            user.IsEnabled = password.IsEnabled = removeCredential.IsEnabled = canEdit && shell.Services.HasCredentialStore;
            checkConnection.IsEnabled = saveConnection.IsEnabled = canEdit;
            preset.IsEnabled = canEdit && shell.CanSelectPreset;
            name.IsEnabled = canEdit && !model.IsHome;
            savePreset.IsEnabled = canEdit && !model.IsHome && model.NameDirty;
            controlPhoto.IsEnabled = canEdit && shell.CanSelectPreset;
            reset.Visibility = shell.Services.NeedsNewSetup ? Visibility.Visible : Visibility.Collapsed;
            reset.IsEnabled = !shell.Services.IsReadOnly && !shell.Services.IsClosing && !model.IsBusy;
            if (shell.Services.NeedsNewSetup) advanced.IsExpanded = true;
        }
        finally { rendering = false; }
    }

    private async Task LoadCredentialAsync()
    {
        if (!shell.Services.HasCredentialStore || credentialReference == shell.Services.Settings.Connection.CredentialTarget) return;
        credentialReference = shell.Services.Settings.Connection.CredentialTarget;
        await model.LoadCredentialUserAsync();
    }

    public void SuspendImages() { loaded = false; shell.Changed -= OnShellChanged; }
    public void ResumeImages()
    {
        loaded = true; shell.Changed -= OnShellChanged; shell.Changed += OnShellChanged; model.Refresh();
    }
    private static void SetId(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
}
