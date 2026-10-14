using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using CameraControl.AppCore;
using CameraControl_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Text;
using Windows.Storage.Streams;

namespace CameraControl_WinUI.Views;

public sealed class PositionsView : UserControl
{
    private readonly ShellViewModel model;
    private readonly Grid layout = new() { RowSpacing = 8, ColumnSpacing = 8, Margin = new Thickness(8) };
    private readonly Dictionary<string, Tile> tiles = new(StringComparer.Ordinal);
    private long imageRevision;
    private AppSettings? renderedSettings;
    private bool loaded;
    private sealed record Tile(Button Button, TextBlock Name, TextBlock State, Image Image, SymbolIcon Placeholder,
        Grid ImageArea, Grid TextArea, Border Selection);

    public PositionsView(ShellViewModel model)
    {
        this.model = model;
        for (var index = 0; index < 2; index++)
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        foreach (var preset in model.Services.Settings.Presets)
        {
            var index = PresetIds.Ordered.ToList().IndexOf(preset.Id);
            var tile = CreateTile(preset.Id);
            Grid.SetRow(tile.Button, index / 2);
            Grid.SetColumn(tile.Button, index % 2);
            layout.Children.Add(tile.Button);
            tiles.Add(preset.Id, tile);
        }
        Content = layout;
        AutomationProperties.SetAutomationId(this, "PositionsPage");
        Unloaded += (_, _) => SuspendRendering();
        Loaded += (_, _) => ResumeRendering();
        SizeChanged += (_, _) => layout.Margin = new Thickness(ActualWidth < 480 ? 8 : 16);
    }

    private Tile CreateTile(string id)
    {
        var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, Visibility = Visibility.Collapsed };
        var placeholder = new SymbolIcon(Symbol.Pictures);
        var imageArea = new Grid();
        imageArea.Children.Add(image);
        imageArea.Children.Add(placeholder);
        var name = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, FontSize = 14 };
        var state = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MinHeight = 19 };
        var text = new Grid { RowSpacing = 4, Margin = new Thickness(8) };
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 19 });
        Grid.SetRow(state, 1);
        text.Children.Add(name);
        text.Children.Add(state);
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(text, 1);
        content.Children.Add(imageArea);
        content.Children.Add(text);
        var selection = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                BorderBrush="{ThemeResource AccentFillColorDefaultBrush}" BorderThickness="2"
                CornerRadius="4" IsHitTestVisible="False" Visibility="Collapsed" />
            """);
        Grid.SetRowSpan(selection, 2);
        content.Children.Add(selection);
        var button = new Button { Content = content, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch };
        var automationId = "Preset" + char.ToUpperInvariant(id[0]) + id[1..];
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetAutomationId(name, automationId + "Name");
        AutomationProperties.SetAutomationId(state, automationId + "State");
        AutomationProperties.SetAutomationId(selection, automationId + "Selection");
        button.Click += async (_, _) =>
        {
            model.SelectPreset(id);
            var preset = model.Services.Settings.Presets.Single(preset => preset.Id == id);
            if (model.CanRecall(preset)) await model.RecallAsync(preset);
        };
        var tile = new Tile(button, name, state, image, placeholder, imageArea, text, selection);
        button.SizeChanged += (_, args) => AdaptTile(tile, args.NewSize.Height);
        return tile;
    }

    private static void AdaptTile(Tile tile, double height)
    {
        if (height <= 0) return;
        var compact = height < 100;
        tile.Name.MaxLines = compact ? 1 : 2;
        tile.Name.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        tile.TextArea.RowSpacing = compact ? 2 : 4;
        tile.TextArea.Margin = compact ? new Thickness(8, 3, 8, 3) : new Thickness(8);
        tile.ImageArea.Visibility = height < 80 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnChanged(object? sender, EventArgs args) => await RefreshAsync();

    public void SuspendRendering() { loaded = false; imageRevision++; model.Changed -= OnChanged; }

    public async void ResumeRendering()
    {
        loaded = true;
        renderedSettings = null;
        model.Changed -= OnChanged;
        model.Changed += OnChanged;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!loaded || model.Services.IsClosing) { imageRevision++; return; }
        foreach (var preset in model.Services.Settings.Presets)
        {
            var tile = tiles[preset.Id];
            tile.Name.Text = preset.Name;
            ToolTipService.SetToolTip(tile.Name, preset.Name);
            tile.State.Text = model.PresetStatus(preset);
            tile.Button.IsEnabled = model.Services.IsInitialized && model.CanSelectPreset;
            var selected = preset.Id == model.SelectedPresetId;
            tile.Selection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(tile.Button, selected ? "Gewählte Position" : "Position wählen und aufrufen, wenn eingerichtet");
            AutomationProperties.SetName(tile.Button, preset.Name + ". " + tile.State.Text);
            ToolTipService.SetToolTip(tile.Button, preset.Name + " — " + tile.State.Text);
        }
        if (ReferenceEquals(renderedSettings, model.Services.Settings)) return;
        renderedSettings = model.Services.Settings;
        var revision = ++imageRevision;
        foreach (var preset in renderedSettings.Presets)
        {
            var tile = tiles[preset.Id];
            var bytes = await model.Services.Repository.TryReadImageAsync(preset.ImageFileName);
            if (revision != imageRevision || !loaded || model.Services.IsClosing) return;
            tile.Image.Source = null;
            tile.Image.Visibility = Visibility.Collapsed;
            tile.Placeholder.Visibility = Visibility.Visible;
            if (bytes is null) continue;
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                if (revision != imageRevision || !loaded || model.Services.IsClosing) return;
                tile.Image.Source = bitmap;
                tile.Image.Visibility = Visibility.Visible;
                tile.Placeholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception error) when (error is ArgumentException or COMException or IOException) { }
        }
    }
}
