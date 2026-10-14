using System.Runtime.InteropServices;
using System.Text.Json;
using CameraControl.WinUI.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 4)
        {
            Console.Error.WriteLine("NativeRunner <QA-EXE> <Belegordner> <DPI|Observe> <Layout|CompactLayout|UiRemaining|LicenseEnvironment|LicenseDialog|HighContrast|Controls|DisplayProbe|ControlsThenDisplay|CleanWindows|UiPages|ReleaseDeployment|ReleasePipeRegression|PhotosAndPreview>");
            return 2;
        }
        SetProcessDpiAwarenessContext(new nint(-4));
        var evidence = Path.GetFullPath(arguments[1]);
        Directory.CreateDirectory(evidence);
        Environment.SetEnvironmentVariable("CAMERA_UI_EXE", Path.GetFullPath(arguments[0]));
        Environment.SetEnvironmentVariable("CAMERA_UI_EVIDENCE_DIRECTORY", evidence);
        Environment.SetEnvironmentVariable("CAMERA_UI_REQUIRED_DPI", arguments[2] == "Observe" ? null : arguments[2]);
        if (arguments[3] is "CompactLayout" or "UiRemaining" or "LicenseEnvironment" or "LicenseDialog" or "Controls" or "ControlsThenDisplay" or "UiPages" or "ReleaseDeployment" or "ReleasePipeRegression" or "PhotosAndPreview" && !Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dieser Eingabenachweis darf nur im isolierten Sandbox-Gast laufen.");
        using var observedWindows = new GuestWindowEvidence(Path.GetFullPath(arguments[0]), evidence);
        var results = new List<QaResult>();
        void Run(string name, Action action)
        {
            try { action(); results.Add(new QaResult(name, true, null)); Console.WriteLine("PASS " + name); }
            catch (Exception error) { results.Add(new QaResult(name, false, error.ToString())); Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        if (arguments[3] == "Layout")
        {
            var tests = new NativeLayoutTests();
            foreach (var theme in new[] { "Light", "Dark" })
            {
                foreach (var state in new[] { "empty", "long" })
                foreach (var minimum in new[] { false, true })
                    Run($"{theme}/{state}/{(minimum ? "minimum" : "target")}", () => tests.FourNativeTilesKeepNamesInsideTheirBounds(theme, state, minimum));
                Run(theme + "/navigation", () => tests.NavigationAndFocusRemainNative(theme));
                Run(theme + "/keyboard-focus", () => tests.KeyboardTabMovesFocusToTheNextNativeField(theme));
            }
            Run("invalid-settings", tests.InvalidSettingsExposeAnExplicitRepairAction);
        }
        else if (arguments[3] == "CompactLayout")
        {
            var tests = new NativeLayoutTests();
            foreach (var theme in new[] { "Light", "Dark" })
                Run(theme + "/target-and-minimum", () => tests.CompactTargetAndMinimum(theme));
        }
        else if (arguments[3] == "UiRemaining")
        {
            var layout = new NativeLayoutTests();
            foreach (var theme in new[] { "Light", "Dark" })
                Run(theme + "/target-and-minimum", () => layout.CompactTargetAndMinimum(theme));
            Run("pointer-stop", new NativeControlTests().PointerReleaseOutsideAndWindowDeactivationSendConfirmedStop);
            Run("license-dialog/minimum", new NativeLicenseTests().LicenseDialogRemainsReadableAndKeyboardScrollableAtMinimumSize);
            Run("guest-display-probe", () => GuestDisplayProbe.Inspect(evidence));
        }
        else if (arguments[3] == "LicenseEnvironment")
        {
            Run("license-dialog/minimum", new NativeLicenseTests().LicenseDialogRemainsReadableAndKeyboardScrollableAtMinimumSize);
            Run("guest-display-probe", () => GuestDisplayProbe.Inspect(evidence));
            var contrast = new NativeHighContrastTests();
            Run("high-contrast/target", () => contrast.RealHighContrastPreservesTheFourTileLayout(false));
            Run("high-contrast/minimum", () => contrast.RealHighContrastPreservesTheFourTileLayout(true));
        }
        else if (arguments[3] == "LicenseDialog")
            Run("license-dialog/minimum", new NativeLicenseTests().LicenseDialogRemainsReadableAndKeyboardScrollableAtMinimumSize);
        else if (arguments[3] == "HighContrast")
        {
            var tests = new NativeHighContrastTests();
            Run("high-contrast/target", () => tests.RealHighContrastPreservesTheFourTileLayout(false));
            Run("high-contrast/minimum", () => tests.RealHighContrastPreservesTheFourTileLayout(true));
        }
        else if (arguments[3] == "DisplayProbe") Run("guest-display-probe", () => GuestDisplayProbe.Inspect(evidence));
        else if (arguments[3] == "CleanWindows") Run("clean-windows", () => CleanWindowsProbe.Run(Path.GetFullPath(arguments[0]), evidence));
        else if (arguments[3] == "UiPages") Run("compact-ui-pages", () => GuestPagesCapture.Run(evidence));
        else if (arguments[3] == "PhotosAndPreview") Run("photos-and-preview", () => GuestPhotosAndPreview.Run(evidence));
        else if (arguments[3] == "ReleaseDeployment") Run("release-deployment", () => GuestReleaseDeployment.Run(Path.GetFullPath(arguments[0]), evidence));
        else if (arguments[3] == "ReleasePipeRegression") Run("release-pipe-regression", () => GuestReleaseDeployment.Run(Path.GetFullPath(arguments[0]), evidence, pipeRegression: true));
        else if (arguments[3] is "Controls" or "ControlsThenDisplay")
        {
            var tests = new NativeControlTests();
            Run("pointer-stop", tests.PointerReleaseOutsideAndWindowDeactivationSendConfirmedStop);
            Run("failed-stop", tests.FailedStopShowsAnErrorAndDisablesPresets);
            if (arguments[3] == "ControlsThenDisplay") Run("guest-display-probe", () => GuestDisplayProbe.Inspect(evidence));
        }
        else return 2;
        File.WriteAllText(Path.Combine(evidence, "runner-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return results.Any(item => !item.Passed) ? 1 : 0;
    }
    private sealed record QaResult(string Name, bool Passed, string? Error);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SetProcessDpiAwarenessContext(nint context);
}
