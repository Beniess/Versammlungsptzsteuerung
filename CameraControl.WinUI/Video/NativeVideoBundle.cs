namespace CameraControl_WinUI.Video;

internal static class NativeVideoBundle
{
    internal static bool TryLocate(out string directory)
    {
        var roots = new List<string> { AppContext.BaseDirectory };
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nativePaths)
            roots.AddRange(nativePaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.GetFullPath(Path.Combine(root, "libvlc", "win-x64"));
            if (File.Exists(Path.Combine(candidate, "libvlc.dll")) && File.Exists(Path.Combine(candidate, "libvlccore.dll")) &&
                Directory.Exists(Path.Combine(candidate, "plugins")))
            {
                directory = candidate;
                return true;
            }
        }
        directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));
        return false;
    }
}
