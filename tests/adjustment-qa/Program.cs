using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CameraControl.WinUI.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 3) return 2;
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(arguments[0]).Equals(@"C:\Payload\App\CameraControl.WinUI.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(arguments[1]).Equals(@"C:\Evidence\Adjustment", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(arguments[2]).Equals(@"C:\Payload\Repo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dieser Nachweis benötigt den eigenen Sandbox-Gast und seine festen QA-Pfade.");
        if (FileVersionInfo.GetVersionInfo(arguments[0]).FileVersion != "1.0.0.5")
            throw new InvalidOperationException("Dieser Nachweis benötigt den eingefrorenen Kandidaten 1.0.0.5.");
        SetProcessDpiAwarenessContext(new nint(-4));
        Directory.CreateDirectory(arguments[1]);
        Environment.SetEnvironmentVariable("CAMERA_UI_EXE", arguments[0]);
        Environment.SetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY", arguments[1]);
        Environment.SetEnvironmentVariable("CAMERA_UI_REPO_ROOT", arguments[2]);
        Environment.SetEnvironmentVariable("CAMERA_UI_REQUIRED_DPI", "96");
        var watch = Stopwatch.StartNew();
        string? failure = null;
        try { new NativeAdjustmentTests().SelectedPositionCanBeAdjustedAndSavedWithoutVideo(); }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine(failure); }
        File.WriteAllText(Path.Combine(arguments[1], "runner-results.json"), JsonSerializer.Serialize(new
        {
            Name = "selected-position-adjustment", Passed = failure is null, Error = failure,
            Milliseconds = watch.ElapsedMilliseconds, RequiredCases = 1, Skipped = 0, HostInputs = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failure is null ? 0 : 1;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);
}
