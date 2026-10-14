using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CameraControl.WinUI.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 3 || !Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) return 2;
        if (!Path.GetFullPath(arguments[0]).Equals(@"C:\Payload\App\CameraControl.WinUI.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(arguments[1]).Equals(@"C:\Evidence\Adjustment", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(arguments[2]).Equals(@"C:\Payload\Repo", StringComparison.OrdinalIgnoreCase) ||
            FileVersionInfo.GetVersionInfo(arguments[0]).FileVersion != "1.0.0.6") return 2;
        SetProcessDpiAwarenessContext(new nint(-4));
        Directory.CreateDirectory(arguments[1]);
        Environment.SetEnvironmentVariable("CAMERA_UI_EXE", arguments[0]);
        Environment.SetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY", arguments[1]);
        Environment.SetEnvironmentVariable("CAMERA_UI_REPO_ROOT", arguments[2]);
        Environment.SetEnvironmentVariable("CAMERA_UI_REQUIRED_DPI", "96");
        var timer = Stopwatch.StartNew(); string? error = null;
        try { new NativePhotoWorkflowTests().ControllerPhotoDialogSavesNamedViewAndCancelPreservesIt(); }
        catch (Exception failure) { error = failure.ToString(); Console.Error.WriteLine(error); }
        File.WriteAllText(Path.Combine(arguments[1], "runner-results.json"), JsonSerializer.Serialize(new
        { Name = "confirmed-photo-workflow", Passed = error is null, Error = error, Milliseconds = timer.ElapsedMilliseconds, RequiredCases = 1, Skipped = 0, HostInputs = false }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
