namespace CameraControl.AppCore.Control;

public readonly record struct PanTiltSpeed(int Pan, int Tilt);

public static class ManualControlMap
{
    public static PanTiltSpeed PanTilt(double x, double y, double width, double height) =>
        new(Normalize(x, width, 24), -Normalize(y, height, 24));
    public static int Zoom(double x, double width) => Normalize(x, width, 7);

    private static int Normalize(double position, double size, int maximum)
    {
        if (!double.IsFinite(position) || !double.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Die Steuerfläche benötigt gültige Koordinaten und eine positive Größe.");
        double scaled = (position / size) * 2 - 1;
        if (scaled <= -1) return -maximum;
        if (scaled >= 1) return maximum;
        return Math.Clamp((int)(scaled * (maximum + 1)), -maximum, maximum);
    }
}
