using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CameraControl.AppCore.Deployment;
using CameraControl_WinUI.Deployment;

namespace CameraControl_WinUI.Startup;

internal static class StartupHost
{
    internal static async Task<InstalledRelease> ReadAsync(string processPath, CancellationToken cancellationToken)
    {
        string path = DeploymentScope.Absolute(processPath);
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Der Start erfordert die veröffentlichte Host-EXE.");
        DeploymentScope.CheckPath(path);
        FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(path);
        ReleaseVersion version = ReleaseVersion.Parse(string.Create(CultureInfo.InvariantCulture,
            $"{metadata.FileMajorPart}.{metadata.FileMinorPart}.{metadata.FileBuildPart}.{metadata.FilePrivatePart}"));
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (file.Length == 0) throw new IOException("Die Host-EXE ist leer.");
        string sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        return new InstalledRelease(version, path, file.Length, sha256);
    }

    internal static void SetAppIdentity(string identity)
    {
        int result = SetCurrentProcessExplicitAppUserModelID(identity);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
