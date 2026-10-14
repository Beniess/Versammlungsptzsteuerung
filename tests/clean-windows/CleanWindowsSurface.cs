using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

internal static class CleanWindowsSurface
{
    internal static bool IsVisible(AutomationElement element)
    {
        var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        return !element.Properties.IsOffscreen.ValueOrDefault && bounds.Width > 0 && bounds.Height > 0;
    }

    internal static void EnterText(Window window, AutomationElement element, string value)
    {
        CleanWindowsProbe.Require(Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase), "Texteingaben sind nur im eigenen Gast erlaubt.");
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        window.Focus();
        if (element.Patterns.ScrollItem.IsSupported) element.Patterns.ScrollItem.Pattern.ScrollIntoView();
        element.Focus();
        CleanWindowsProbe.WaitUntil(() => GetForegroundWindow() == handle && element.Properties.HasKeyboardFocus.ValueOrDefault &&
            IsVisible(element) && element.Properties.IsEnabled.ValueOrDefault, "Das eigene Eingabefeld besitzt keinen sichtbaren Tastaturfokus.", 15);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
        CleanWindowsProbe.WaitUntil(() => element.AsTextBox().Text == value, "Die tatsächliche Texteingabe stimmt nicht mit dem Testwert überein.", 15);
        Keyboard.Type(VirtualKeyShort.TAB);
        CleanWindowsProbe.WaitUntil(() => !element.Properties.HasKeyboardFocus.ValueOrDefault && element.AsTextBox().Text == value,
            "Der Fokuswechsel hat die eingegebene Zeichenfolge nicht erhalten.", 15);
    }

    internal static void SearchAndLaunch(UIA3Automation automation, string evidence, string name)
    {
        CleanWindowsProbe.Require(Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase), "Sucheingaben sind nur im eigenen Gast erlaubt.");
        OpenTaskbarSearch(automation, evidence, name);
        Window? search = null;
        CleanWindowsProbe.WaitUntil(() =>
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId == 0) return false;
            using var process = Process.GetProcessById((int)processId);
            var executable = process.MainModule?.FileName;
            if (executable is null || !IsWindowsSearch(executable)) return false;
            search = automation.FromHandle(foreground).AsWindow();
            return IsWindowVisible(foreground) && Responds(foreground);
        }, "Die echte Windows-Suchoberfläche wurde nicht aktiv.", 30);
        SaveTree(search!, Path.Combine(evidence, name + "-before-query.json"));
        var fields = search!.FindAllDescendants().Where(element => IsVisible(element) && element.Properties.IsEnabled.ValueOrDefault &&
            element.Properties.ControlType.ValueOrDefault == ControlType.Edit && element.Patterns.Value.IsSupported).ToArray();
        CleanWindowsProbe.Write(evidence, name + "-query-fields.json", fields.Select(element => new
        {
            Name = element.Properties.Name.ValueOrDefault, Id = element.Properties.AutomationId.ValueOrDefault,
            Type = element.Properties.ControlType.ValueOrDefault.ToString(), Focused = element.Properties.HasKeyboardFocus.ValueOrDefault
        }).ToArray());
        var focusedFields = fields.Where(element => element.Properties.HasKeyboardFocus.ValueOrDefault).ToArray();
        var field = focusedFields.Length == 1 ? focusedFields[0] : fields.Length == 1 ? fields[0] : null;
        CleanWindowsProbe.Require(field is not null, "Der beobachtete Suchbaum enthält kein eindeutiges sichtbares Such-Eingabefeld.");
        field!.Focus();
        CleanWindowsProbe.WaitUntil(() => field.Properties.HasKeyboardFocus.ValueOrDefault &&
            GetForegroundWindow() == search.Properties.NativeWindowHandle.ValueOrDefault, "Das beobachtete Windows-Suchfeld besitzt keinen tatsächlichen Tastaturfokus.", 15);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(CleanWindowsProbe.AppName);
        CleanWindowsProbe.WaitUntil(() => field.AsTextBox().Text == CleanWindowsProbe.AppName,
            "Die tatsächliche Windows-Suchabfrage stimmt nicht mit dem Produktnamen überein.", 15);
        CleanWindowsProbe.Write(evidence, name + "-query-confirmed.json", new { Query = field.AsTextBox().Text,
            Name = field.Properties.Name.ValueOrDefault, Id = field.Properties.AutomationId.ValueOrDefault,
            KeyboardFocused = field.Properties.HasKeyboardFocus.ValueOrDefault, ActualFieldObserved = true });
        AutomationElement? result = null;
        var timer = Stopwatch.StartNew();
        try
        {
            CleanWindowsProbe.WaitUntil(() =>
            {
                result = search!.FindAllDescendants().FirstOrDefault(element => IsVisible(element) && element.Properties.IsEnabled.ValueOrDefault &&
                    element.Properties.Name.ValueOrDefault?.Contains(CleanWindowsProbe.AppName, StringComparison.OrdinalIgnoreCase) == true &&
                    element.Properties.ControlType.ValueOrDefault is ControlType.Button or ControlType.ListItem or ControlType.TreeItem &&
                    (element.Patterns.Invoke.IsSupported || element.Patterns.SelectionItem.IsSupported));
                return result is not null;
            }, "Die echte Windows-Suche hat keinen aktivierbaren Produkteintrag innerhalb von 120 Sekunden gefunden.", 120);
            SaveTree(search!, Path.Combine(evidence, name + ".json"));
            Capture(search!, Path.Combine(evidence, name + ".png"), fromScreen: true);
            GetWindowThreadProcessId(search!.Properties.NativeWindowHandle.ValueOrDefault, out var searchPid);
            CleanWindowsProbe.Write(evidence, name + "-selection.json", new { SearchProcessId = searchPid, SearchHandle = search.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(),
                Query = CleanWindowsProbe.AppName, ResultName = result!.Properties.Name.ValueOrDefault, ResultType = result.Properties.ControlType.ValueOrDefault.ToString(),
                ResultAutomationId = result.Properties.AutomationId.ValueOrDefault, SearchMilliseconds = timer.ElapsedMilliseconds,
                DirectLinkLaunch = false, DirectExeLaunch = false });
            if (result.Patterns.Invoke.IsSupported) result.Patterns.Invoke.Pattern.Invoke();
            else { result.Patterns.SelectionItem.Pattern.Select(); result.Focus(); Keyboard.Type(VirtualKeyShort.RETURN); }
        }
        catch
        {
            SaveTree(search!, Path.Combine(evidence, name + "-failed.json"));
            Capture(search!, Path.Combine(evidence, name + "-failed.png"), fromScreen: true);
            throw;
        }
    }

    private static void OpenTaskbarSearch(UIA3Automation automation, string evidence, string name)
    {
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var taskbars = automation.GetDesktop().FindAllChildren().Where(element =>
        {
            if (!IsVisible(element) || element.Properties.Name.ValueOrDefault is not ("Taskleiste" or "Taskbar")) return false;
            var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !IsWindowVisible(handle)) return false;
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.MainModule?.FileName, explorerPath, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        CleanWindowsProbe.Require(taskbars.Length == 1, "Die echte Gast-Taskleiste ist nicht eindeutig verfügbar.");
        var taskbar = taskbars[0].AsWindow();
        SaveTree(taskbar, Path.Combine(evidence, name + "-taskbar-tree.json"));
        var candidates = taskbar.FindAllDescendants().Where(element => IsVisible(element) && element.Properties.IsEnabled.ValueOrDefault &&
            element.Properties.ControlType.ValueOrDefault == ControlType.Button && System.Text.RegularExpressions.Regex.IsMatch(element.Properties.Name.ValueOrDefault ?? "",
                @"^(Suche|Suchen|Search)(?:$|[\s,:(])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)).ToArray();
        CleanWindowsProbe.Write(evidence, name + "-taskbar-candidates.json", candidates.Select(element => new
        {
            Name = element.Properties.Name.ValueOrDefault, AutomationId = element.Properties.AutomationId.ValueOrDefault,
            ClassName = element.Properties.ClassName.ValueOrDefault, RuntimeId = element.Properties.RuntimeId.ValueOrDefault,
            Type = element.Properties.ControlType.ValueOrDefault.ToString(), InvokeSupported = element.Patterns.Invoke.IsSupported
        }).ToArray());
        CleanWindowsProbe.Require(candidates.Length == 1, "Der beobachtete Taskleistenbaum enthält keinen eindeutigen sichtbaren Suchbutton.");
        var button = candidates[0];
        var taskbarHandle = taskbar.Properties.NativeWindowHandle.ValueOrDefault;
        GetWindowThreadProcessId(taskbarHandle, out var taskbarProcessId);
        CleanWindowsProbe.Write(evidence, name + "-taskbar-selector.json", new
        {
            ObservedUtc = DateTimeOffset.UtcNow, TaskbarProcessId = taskbarProcessId, TaskbarHandle = taskbarHandle.ToInt64(),
            TaskbarName = taskbar.Properties.Name.ValueOrDefault, TaskbarExecutable = explorerPath,
            Name = button.Properties.Name.ValueOrDefault, AutomationId = button.Properties.AutomationId.ValueOrDefault,
            ClassName = button.Properties.ClassName.ValueOrDefault, RuntimeId = button.Properties.RuntimeId.ValueOrDefault,
            Type = button.Properties.ControlType.ValueOrDefault.ToString(), Action = button.Patterns.Invoke.IsSupported ? "UIA.Invoke" : "UIA.Element.Click",
            WindowsKeyUsed = false
        });
        if (button.Patterns.Invoke.IsSupported) button.Patterns.Invoke.Pattern.Invoke();
        else button.Click();
    }

    private static bool IsWindowsSearch(string path)
    {
        var name = Path.GetFileName(path);
        return name is "SearchHost.exe" or "SearchApp.exe" or "StartMenuExperienceHost.exe" &&
            (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    internal static nint[] OwnVisibleWindows(int processId)
    {
        var windows = new List<nint>();
        EnumWindows((window, _) => { GetWindowThreadProcessId(window, out var pid); if (pid == processId && IsWindowVisible(window)) windows.Add(window); return true; }, 0);
        return windows.ToArray();
    }

    internal static bool Responds(nint handle) => SendMessageTimeout(handle, 0, 0, 0, 2, 2000, out _) != 0;
    internal static void RequestClose(nint handle) => PostMessage(handle, 0x0010, 0, 0);

    internal static void Capture(Window window, string path, bool fromScreen = false)
    {
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        CleanWindowsProbe.Require(GetWindowRect(handle, out var bounds), "Die Fenstergrenzen sind nicht verfügbar.");
        CleanWindowsProbe.Require(IsWindowVisible(handle) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top, "Das geprüfte Fenster ist nicht sichtbar.");
        using var image = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = Graphics.FromImage(image);
        if (fromScreen)
        {
            CleanWindowsProbe.Require(GetForegroundWindow() == handle, "Das aufzunehmende Suchfenster steht nicht im Vordergrund.");
            graphics.CopyFromScreen(new Point(bounds.Left, bounds.Top), Point.Empty, image.Size);
        }
        else
        {
            var dc = graphics.GetHdc();
            try { CleanWindowsProbe.Require(PrintWindow(handle, dc, 2), "PrintWindow hat keinen Fensterbeleg erzeugt."); }
            finally { graphics.ReleaseHdc(dc); }
        }
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        var colors = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 8) for (var x = 0; x < image.Width; x += 8) colors.Add(image.GetPixel(x, y).ToArgb());
        CleanWindowsProbe.Require(colors.Count > 3, "Der Fensterbeleg enthält keine prüfbare Oberfläche.");
        GetWindowThreadProcessId(handle, out var processId);
        CleanWindowsProbe.Write(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-window.json", new
        {
            ProcessId = processId, Handle = handle.ToInt64(), Visible = true, Responsive = Responds(handle), Dpi = GetDpiForWindow(handle),
            Width = image.Width, Height = image.Height, SampledColors = colors.Count, Title = window.Properties.Name.ValueOrDefault, FromGuestScreen = fromScreen
        });
    }

    internal static void SaveTree(Window window, string path)
    {
        GetWindowThreadProcessId(window.Properties.NativeWindowHandle.ValueOrDefault, out var processId);
        CleanWindowsProbe.Write(Path.GetDirectoryName(path)!, Path.GetFileName(path), new { ProcessId = processId,
            Hwnd = window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(), Elements = window.FindAllDescendants().Prepend(window).Select(element => new
            {
                Name = element.Properties.Name.ValueOrDefault, Id = element.Properties.AutomationId.ValueOrDefault,
                Type = element.Properties.ControlType.ValueOrDefault.ToString(), Enabled = element.Properties.IsEnabled.ValueOrDefault,
                Offscreen = element.Properties.IsOffscreen.ValueOrDefault
            }).ToArray() });
    }

    internal static void SaveDesktopEvidence(UIA3Automation automation, string evidence, string prefix)
    {
        try
        {
            var windows = automation.GetDesktop().FindAllChildren();
            CleanWindowsProbe.Write(evidence, prefix + "-desktop.json", windows.Select(element => new
            {
                Name = element.Properties.Name.ValueOrDefault, Id = element.Properties.ProcessId.ValueOrDefault,
                Hwnd = element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(), Offscreen = element.Properties.IsOffscreen.ValueOrDefault
            }).ToArray());
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bitmap = new Bitmap(screen.Width, screen.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
            bitmap.Save(Path.Combine(evidence, prefix + "-desktop.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { File.WriteAllText(Path.Combine(evidence, prefix + "-capture-error.txt"), error.ToString()); }
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
}
