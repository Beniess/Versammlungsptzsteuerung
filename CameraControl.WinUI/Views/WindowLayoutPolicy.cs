using CameraControl.AppCore;

namespace CameraControl_WinUI.Views;

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

public static class WindowLayoutPolicy
{
    public const double MinimumWidthDip = 360;
    public const double MinimumHeightDip = 280;

    public static PixelBounds Restore(WindowPlacement? saved, double scale, PixelBounds workArea)
    {
        if (!double.IsFinite(scale) || scale <= 0 || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        var width = saved is null ? 600 : saved.Width * scale / saved.Scale;
        var height = saved is null ? 430 : saved.Height * scale / saved.Scale;
        var actualWidth = (int)Math.Min(workArea.Width, Math.Max(Math.Ceiling(MinimumWidthDip * scale), Math.Round(width)));
        var actualHeight = (int)Math.Min(workArea.Height, Math.Max(Math.Ceiling(MinimumHeightDip * scale), Math.Round(height)));
        var x = saved?.X ?? workArea.X + 16 * scale;
        var y = saved?.Y ?? workArea.Y + 16 * scale;
        return new PixelBounds(
            (int)Math.Clamp(Math.Round(x), workArea.X, workArea.X + workArea.Width - actualWidth),
            (int)Math.Clamp(Math.Round(y), workArea.Y, workArea.Y + workArea.Height - actualHeight),
            actualWidth, actualHeight);
    }
}
