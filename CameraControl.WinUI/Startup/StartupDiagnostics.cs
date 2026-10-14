using System.Diagnostics;
using System.Text.Json;
using CameraControl.AppCore.Deployment;
using CameraControl_WinUI.Deployment;

namespace CameraControl_WinUI.Startup;

internal sealed class StartupDiagnostics(StartupOptions options, InstalledRelease host)
{
    private readonly object sync = new();

    internal void RecordEvent(string value)
    {
        if (options.Scope?.QaRoot is not string root) return;
        lock (sync)
        {
            string directory = Path.Combine(root, "Diagnostics");
            string path = Path.Combine(directory, Environment.ProcessId + ".events.jsonl");
            try
            {
                DeploymentScope.CheckPath(path);
                Directory.CreateDirectory(directory);
                File.AppendAllText(path, JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow,
                    processId = Environment.ProcessId, version = host.Version.ToString(), detail = value }) + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error.Message); }
        }
    }

    internal void Record(string phase, bool readOnly, nint windowHandle = 0, string? error = null)
    {
        if (options.Scope?.QaRoot is not string root) return;
        lock (sync)
        {
            string directory = Path.Combine(root, "Diagnostics");
            string target = Path.Combine(directory, Environment.ProcessId + ".json");
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                DeploymentScope.CheckPath(directory);
                DeploymentScope.CheckPath(target);
                Directory.CreateDirectory(directory);
                using (var pendingStream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(pendingStream, new
                {
                    phase, processId = Environment.ProcessId, processPath = host.ExecutablePath,
                    version = host.Version.ToString(), sha256 = host.Sha256, readOnly,
                    windowHandle = windowHandle.ToInt64(), dataDirectory = options.DataDirectory, isQa = options.IsQa,
                    error, recordedUtc = DateTimeOffset.UtcNow
                });
                File.Move(pending, target, overwrite: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { Debug.WriteLine("QA-Startstatus konnte nicht geschrieben werden: " + failure.Message); }
            finally
            {
                try { if (File.Exists(pending)) File.Delete(pending); }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { Debug.WriteLine(failure.Message); }
            }
        }
    }
}
