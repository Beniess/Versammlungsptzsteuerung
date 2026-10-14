using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CameraControl_WinUI.Views;

public static class LicenseView
{
    private sealed record DocumentChoice(string Title, string Path, LicenseComponent? Component = null);

    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var catalog = LicenseCatalog.Load();
        var executable = Environment.ProcessPath;
        var version = executable is null ? "Unbekannt" : FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "Unbekannt";
        var choices = new List<DocumentChoice>
        {
            new("Drittsoftware-Hinweise", catalog.NoticesPath),
            new("Quellen und Bibliotheksaustausch", "Sources-and-replacement.md"),
            new("Originaler Dateiindex", "index.json")
        };
        foreach (var component in catalog.Components)
            foreach (var file in component.Files.Where(file => file.ReadableText))
                choices.Add(new DocumentChoice(component.Name + " " + component.Version + " — " + Path.GetFileName(file.Path), file.Path, component));

        var versionText = new TextBlock { Text = "Version " + version + " · Lizenzen", FontSize = 20,
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        var picker = new ComboBox { ItemsSource = choices, DisplayMemberPath = nameof(DocumentChoice.Title),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 14 };
        var scroll = new ScrollViewer { Content = text, IsTabStop = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new Grid { RowSpacing = 8, Width = Math.Clamp(xamlRoot.Size.Width - 64, 200, 480),
            Height = Math.Clamp(xamlRoot.Size.Height - 136, 96, 360) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(scroll, 1);
        content.Children.Add(picker); content.Children.Add(scroll);
        AutomationProperties.SetAutomationId(versionText, "LicenseVersion");
        AutomationProperties.SetAutomationId(picker, "LicenseEntry");
        AutomationProperties.SetName(picker, "Lizenztext oder Quellenhinweis wählen");
        AutomationProperties.SetAutomationId(text, "LicenseText");
        AutomationProperties.SetAutomationId(scroll, "LicenseDocumentScroll");
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not DocumentChoice choice) return;
            var header = "";
            if (choice.Component is { } component)
            {
                header = component.Name + " " + component.Version + "\nLizenz: " + component.License + "\n" + component.Copyright +
                    "\nQuelle: " + component.Source + "\nDatei: " + choice.Path + "\n";
                if (!string.IsNullOrWhiteSpace(component.Scope)) header += "Zuordnung: " + component.Scope + "\n";
                var selectedFile = component.Files.First(file => file.Path == choice.Path);
                if (!string.IsNullOrWhiteSpace(selectedFile.SourceArchive)) header += "Quellarchiv: " + selectedFile.SourceArchive + "\n";
                if (!string.IsNullOrWhiteSpace(selectedFile.SourceEntry)) header += "Originalpfad im Archiv: " + selectedFile.SourceEntry + "\n";
                if (!string.IsNullOrWhiteSpace(selectedFile.Extraction)) header += "Originalauszug: " + selectedFile.Extraction + "\n";
                foreach (var original in component.Files.Where(file => !file.ReadableText))
                    header += "Zusätzliches unverändertes Original im Bundle: " + original.Path + "\nSHA-256: " + original.Sha256 + "\n";
                header += "\n";
            }
            text.Text = header + catalog.ReadText(choice.Path);
            scroll.ChangeView(null, 0, null);
        };
        picker.SelectedIndex = 0;
        var dialog = new ContentDialog { Title = versionText, Content = content, XamlRoot = xamlRoot,
            CloseButtonText = "Schließen", DefaultButton = ContentDialogButton.Close,
            RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default };
        AutomationProperties.SetAutomationId(dialog, "LicensesDialog");
        AutomationProperties.SetName(dialog, "Version und Lizenzen");
        if (xamlRoot.IsHostVisible) await dialog.ShowAsync();
    }
}
