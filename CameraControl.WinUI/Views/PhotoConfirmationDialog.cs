using System.Runtime.InteropServices.WindowsRuntime;
using CameraControl.AppCore;
using CameraControl.AppCore.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CameraControl_WinUI.Views;

public static class PhotoConfirmationDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root, PhotoConfirmation photo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(photo.Png.ToArray().AsBuffer());
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform,
            Height = Math.Clamp(root.Size.Height - 240, 64, 240),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(image);
        content.Children.Add(new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Text = photo.PresetId == PresetIds.Home ? "Das Foto wird für Home gespeichert."
                : "Das Foto und die aktuelle Kameraposition werden für diese Ansicht gespeichert." });
        var title = $"Foto für „{photo.Name}“ speichern?";
        var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(48, root.Size.Height - 188),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog { XamlRoot = root,
            Title = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, Content = scroll,
            PrimaryButtonText = "Speichern", CloseButtonText = "Abbrechen", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default };
        AutomationProperties.SetAutomationId(dialog, "PhotoSaveDialog");
        AutomationProperties.SetName(dialog, title);
        AutomationProperties.SetAutomationId(image, "PhotoSaveImage");
        AutomationProperties.SetName(image, "Aufgenommenes Foto für " + photo.Name);
        dialog.Loaded += (_, _) => SetButtonIds(dialog);
        using var registration = cancellationToken.Register(() => dialog.DispatcherQueue.TryEnqueue(dialog.Hide));
        void Fit()
        {
            content.Width = Math.Clamp(root.Size.Width - 96, 192, 480);
            image.Height = Math.Clamp(root.Size.Height - 240, 48, 240);
            scroll.MaxHeight = Math.Max(32, root.Size.Height - 188);
        }
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();
        Fit();
        root.Changed += OnRootChanged;
        try
        {
            if (!root.IsHostVisible || cancellationToken.IsCancellationRequested) return false;
            var result = await dialog.ShowAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return result == ContentDialogResult.Primary;
        }
        finally { root.Changed -= OnRootChanged; }
    }

    private static void SetButtonIds(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { Name: "PrimaryButton" } primary)
                AutomationProperties.SetAutomationId(primary, "PhotoSaveConfirm");
            if (child is Button { Name: "CloseButton" } cancel)
                AutomationProperties.SetAutomationId(cancel, "PhotoSaveCancel");
            SetButtonIds(child);
        }
    }
}
