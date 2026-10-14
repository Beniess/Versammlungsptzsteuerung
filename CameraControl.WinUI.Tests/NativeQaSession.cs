using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CameraControl.AppCore;
using CameraControl.AppCore.Storage;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

public sealed class NativeQaSession : IDisposable
{
    public readonly UIA3Automation Automation = new();
    public readonly FlaUI.Core.Application Application;
    public readonly Window Window;
    public string EvidenceDirectory { get; }
    public string DataDirectory { get; }
    public uint Dpi => GetDpiForWindow(Window.Properties.NativeWindowHandle.ValueOrDefault);
    public double Scale => Dpi / 96.0;
    private readonly string executable;
    private readonly string theme;
    private readonly string bundleCache;
    private readonly bool ownsBundleCache;
    private readonly string cacheRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Versammlungsptzsteuerung-NativeQA"));

    public NativeQaSession(string name, string theme = "System", string state = "empty", int? cameraPort = null,
        ConnectionSettings? connection = null, string? bundleCache = null)
    {
        this.theme = theme;
        ownsBundleCache = bundleCache is null;
        this.bundleCache = Path.GetFullPath(bundleCache ?? Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")));
        executable = RequiredPath("CAMERA_UI_EXE");
        EvidenceDirectory = Path.Combine(RequiredPath("CAMERA_UI_EVIDENCE_DIRECTORY"), name);
        DataDirectory = Path.Combine(EvidenceDirectory, "qa-data");
        Directory.CreateDirectory(DataDirectory);
        var settings = AppSettings.CreateDefault();
        if (state == "long") settings = settings with { Presets = settings.Presets.Select(value => value.Id == PresetIds.Home ? value :
            value with { Name = "Die ausführliche Bezeichnung für die Versammlungsposition " + value.Name, Target = new CameraPosition(100, 200, 300) }).ToArray() };
        if (cameraPort.HasValue) settings = settings with { Connection = new ConnectionSettings("127.0.0.1", cameraPort.Value, null, null) };
        if (connection is not null) settings = settings with { Connection = connection };
        if (state == "invalid") File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), "{ invalid");
        else if (state != "empty" || cameraPort.HasValue || connection is not null) new JsonSettingsRepository(DataDirectory).SaveAsync(settings).GetAwaiter().GetResult();
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
        start.Environment["CAMERA_CONTROL_QA_MODE"] = "1";
        start.Environment["CAMERA_CONTROL_QA_DATA_DIRECTORY"] = DataDirectory;
        start.Environment["CAMERA_CONTROL_QA_THEME"] = theme;
        start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = this.bundleCache;
        Application = FlaUI.Core.Application.Launch(start);
        try
        {
            Window = WaitForWindow();
            Assert.That(Window.Title, Does.Not.Contain("VideoProbe"));
            WaitUntil(() => Window.FindFirstDescendant(condition => condition.ByAutomationId("PresetHome")) is not null,
                "Die eigene Shell muss vollständig bereit sein.");
            Window.Focus();
            var expected = Environment.GetEnvironmentVariable("CAMERA_UI_REQUIRED_DPI");
            if (expected is not null) Assert.That(Dpi, Is.EqualTo(uint.Parse(expected)), "Die tatsächliche DPI weicht von der angeforderten Stufe ab.");
        }
        catch { try { CloseAndClean(); } finally { Application.Dispose(); Automation.Dispose(); } throw; }
    }

    public AutomationElement Find(string id) => Window.FindFirstDescendant(condition => condition.ByAutomationId(id)) ??
        throw new AssertionException("Das native Element fehlt: " + id);

    public void Resize(int width, int height)
    {
        var handle = Window.Properties.NativeWindowHandle.ValueOrDefault;
        WithPhysicalDpi(() =>
        {
            Assert.That(GetWindowRect(handle, out var bounds), Is.True);
            Assert.That(MoveWindow(handle, bounds.Left, bounds.Top, width, height, true), Is.True);
        });
        WaitUntil(() => WithPhysicalDpi(() => { GetWindowRect(handle, out var bounds); return bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height; }),
            "Das eigene Fenster muss das angeforderte physische Maß erreichen.");
    }

    public void Capture(string state)
    {
        var handle = Window.Properties.NativeWindowHandle.ValueOrDefault;
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        DwmFlush();
        Thread.Sleep(200);
        DwmFlush();
        var path = Path.Combine(EvidenceDirectory, $"{Dpi}dpi-{theme}-{state}.png");
        WindowRectangle bounds = default;
        double surfaceBrightness = 0;
        WithPhysicalDpi(() =>
        {
            Assert.That(GetWindowRect(handle, out bounds), Is.True);
            using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            using var graphics = Graphics.FromImage(bitmap);
            var dc = graphics.GetHdc();
            try { Assert.That(PrintWindow(handle, dc, 2), Is.True, "Das eigene HWND muss sichtbare Pixel liefern."); }
            finally { graphics.ReleaseHdc(dc); }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var colors = new HashSet<int>();
            for (var y = 32; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 50))
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 50)) colors.Add(bitmap.GetPixel(x, y).ToArgb());
            Assert.That(colors.Count, Is.GreaterThan(3), "Eine schwarze oder leere Aufnahme ist kein UI-Nachweis.");
            var surface = Enumerable.Range(2, 16).Select(index => bitmap.GetPixel(bitmap.Width * index / 20, bitmap.Height - 12).GetBrightness()).Order().ToArray();
            surfaceBrightness = surface[surface.Length / 2];
            if (theme == "Light") Assert.That(surfaceBrightness, Is.GreaterThan(.5), "Die native helle Systemfläche muss tatsächlich gerendert sein.");
            if (theme == "Dark") Assert.That(surfaceBrightness, Is.LessThan(.5), "Die native dunkle Systemfläche muss tatsächlich gerendert sein.");
        });
        using var file = File.OpenRead(executable);
        var record = new
        {
            State = state, Theme = theme, ProcessId = Application.ProcessId, Executable = executable,
            ExecutableVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion,
            ExecutableSha256 = Convert.ToHexString(SHA256.HashData(file)), QaDataDirectory = DataDirectory,
            BundleCache = bundleCache,
            WindowTitle = Window.Title, WindowHandle = handle.ToInt64(), Dpi, Scale,
            Bounds = new { X = bounds.Left, Y = bounds.Top, Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top },
            Screenshot = path, CaptureApi = "PrintWindow(PW_RENDERFULLCONTENT=2)",
            SurfaceBrightness = surfaceBrightness,
            Elements = Window.FindAllDescendants().Select(element => new
            {
                Id = element.Properties.AutomationId.ValueOrDefault, Name = element.Properties.Name.ValueOrDefault,
                Type = element.Properties.ControlType.ValueOrDefault.ToString(), IsEnabled = element.Properties.IsEnabled.ValueOrDefault,
                IsOffscreen = element.Properties.IsOffscreen.ValueOrDefault,
                Bounds = Rect(element)
            }).ToArray()
        };
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static object Rect(AutomationElement element)
    {
        var rectangle = element.Properties.BoundingRectangle.ValueOrDefault;
        return new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
    }

    public static void WaitUntil(Func<bool> condition, string message, int seconds = 10)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds)) { if (condition()) return; Thread.Sleep(100); }
        Assert.Fail(message);
    }

    private Window WaitForWindow()
    {
        Window? result = null;
        WaitUntil(() =>
        {
            Assert.That(Application.HasExited, Is.False, "Die eigene QA-Instanz endete vor dem Fenster.");
            result = Application.GetMainWindow(Automation, TimeSpan.FromMilliseconds(500));
            return result is not null && !result.IsOffscreen;
        }, "Kein sichtbares eigenes Fenster innerhalb von 60 Sekunden.", 60);
        return result!;
    }

    public void Dispose()
    {
        try { CloseAndClean(); }
        finally { Application.Dispose(); Automation.Dispose(); }
    }

    private void CloseAndClean()
    {
        var processId = Application.ProcessId;
        var exited = Application.HasExited || Application.Close(killIfCloseFails: false);
        if (exited && !Application.HasExited)
        {
            var timer = Stopwatch.StartNew();
            while (!Application.HasExited && timer.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(100);
            exited = Application.HasExited;
        }
        var cacheRemoved = false;
        if (exited && ownsBundleCache)
        {
            var resolved = Path.GetFullPath(bundleCache);
            Assert.That(resolved.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True,
                "Nur der geprüfte eigene QA-Cache darf entfernt werden.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
            cacheRemoved = !Directory.Exists(resolved);
        }
        File.WriteAllText(Path.Combine(EvidenceDirectory, "process-cleanup.json"), JsonSerializer.Serialize(new
        { ProcessId = processId, Exited = exited, BundleCache = bundleCache, OwnedBundleCache = ownsBundleCache, CacheRemoved = cacheRemoved },
            new JsonSerializerOptions { WriteIndented = true }));
        Assert.That(exited, Is.True, "Die eigene QA-Instanz muss geordnet schließen.");
    }

    private static string RequiredPath(string variable) => Path.GetFullPath(Environment.GetEnvironmentVariable(variable) ??
        throw new AssertionException(variable + " fehlt. Verwende Test-Desktop.ps1."));
    private static void WithPhysicalDpi(Action action) => WithPhysicalDpi(() => { action(); return true; });
    private static T WithPhysicalDpi<T>(Func<T> action)
    {
        var previous = SetThreadDpiAwarenessContext(new nint(-4));
        try { return action(); }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
