namespace CameraControl_WinUI.Video;

public sealed record VideoProbeOptions(string OutputDirectory, Uri RtspAddress)
{
    public static VideoProbeOptions? Read(string[] arguments)
    {
        var probeIndex = Array.IndexOf(arguments, "--video-probe");
        if (probeIndex < 0) return null;
        var addressIndex = Array.IndexOf(arguments, "--rtsp");
        if (probeIndex + 1 >= arguments.Length || addressIndex < 0 || addressIndex + 1 >= arguments.Length)
            throw new ArgumentException("Die lokale Video-Prüfung benötigt einen Ausgabeordner und eine RTSP-Adresse.");
        var address = new Uri(arguments[addressIndex + 1], UriKind.Absolute);
        if (address.Scheme != "rtsp" || !address.IsLoopback)
            throw new ArgumentException("Die Video-Prüfung erlaubt ausschließlich RTSP an Loopback.");
        return new VideoProbeOptions(Path.GetFullPath(arguments[probeIndex + 1]), address);
    }
}
