using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

internal static class GuestDisplayProbe
{
    internal static void Inspect(string evidence)
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Die Anzeigenprobe darf nur im isolierten Sandbox-Gast laufen.");
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
        using var automation = new UIA3Automation();
        Window? settings = null;
        var timer = Stopwatch.StartNew();
        while (settings is null && timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            var processIds = Process.GetProcessesByName("SystemSettings").Select(process => { using (process) return process.Id; }).ToHashSet();
            var windows = automation.GetDesktop().FindAllChildren();
            File.WriteAllText(Path.Combine(evidence, "display-window-inventory.json"), JsonSerializer.Serialize(new
            {
                SystemSettingsPids = processIds,
                Windows = windows.Select(element => new { Name = element.Name, ProcessId = element.Properties.ProcessId.ValueOrDefault,
                    NativeProcessId = NativeProcessId(element), Handle = element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(),
                    IsOffscreen = element.Properties.IsOffscreen.ValueOrDefault }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            settings = windows.Where(element => !element.Properties.IsOffscreen.ValueOrDefault && (processIds.Contains((int)NativeProcessId(element)) ||
                    element.Name is "Einstellungen" or "Settings"))
                .Select(element => element.AsWindow()).FirstOrDefault();
            if (settings is null) Thread.Sleep(200);
        }
        if (settings is null) throw new InvalidOperationException("Die native Gast-Anzeigeeinstellung ist nicht erreichbar.");
        settings.Focus();
        Thread.Sleep(1000);
        SaveTree(automation, settings, Path.Combine(evidence, "display-initial.json"));
        Capture(settings, Path.Combine(evidence, "display-initial.png"));
        var system = settings.FindAllDescendants().FirstOrDefault(element =>
            element.Properties.ControlType.ValueOrDefault == ControlType.ListItem &&
            element.Properties.Name.ValueOrDefault == "System");
        if (system is not null)
        {
            if (system.Patterns.SelectionItem.IsSupported) system.Patterns.SelectionItem.Pattern.Select();
            else if (system.Patterns.Invoke.IsSupported) system.Patterns.Invoke.Pattern.Invoke();
            else throw new InvalidOperationException("Der beobachtete System-Eintrag bietet kein natives Navigationsmuster.");
            Thread.Sleep(1000);
            SaveTree(automation, settings, Path.Combine(evidence, "display-after-system.json"));
            Capture(settings, Path.Combine(evidence, "display-after-system.png"));
            var display = settings.FindAllDescendants().FirstOrDefault(element => element.Patterns.Invoke.IsSupported &&
                element.Properties.Name.ValueOrDefault is { } name &&
                (name.StartsWith("Bildschirm", StringComparison.Ordinal) || name.StartsWith("Anzeige", StringComparison.Ordinal) || name.StartsWith("Display", StringComparison.Ordinal)));
            if (display is not null)
            {
                display.Patterns.Invoke.Pattern.Invoke();
                Thread.Sleep(1000);
                SaveTree(automation, settings, Path.Combine(evidence, "display-after-navigation.json"));
                Capture(settings, Path.Combine(evidence, "display-after-navigation.png"));
            }
        }
        var index = 0;
        foreach (var combo in settings.FindAllDescendants(condition => condition.ByControlType(ControlType.ComboBox)))
        {
            index++;
            if (!combo.Properties.IsEnabled.ValueOrDefault || !combo.Patterns.ExpandCollapse.IsSupported) continue;
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            Thread.Sleep(500);
            SaveTree(automation, settings, Path.Combine(evidence, $"display-combo-{index}.json"));
            Capture(settings, Path.Combine(evidence, $"display-combo-{index}.png"));
            combo.Patterns.ExpandCollapse.Pattern.Collapse();
        }
        InspectContrastOptions(automation, settings, evidence);
    }

    private static void InspectContrastOptions(UIA3Automation automation, Window settings, string evidence)
    {
        var actions = new List<string>();
        try
        {
            var accessibility = settings.FindAllDescendants().FirstOrDefault(element =>
                element.Properties.ControlType.ValueOrDefault == ControlType.ListItem &&
                element.Properties.Name.ValueOrDefault is "Barrierefreiheit" or "Accessibility");
            if (accessibility is null) throw new InvalidOperationException("Kein Barrierefreiheitseintrag im tatsächlichen Settings-Baum.");
            if (accessibility.Patterns.SelectionItem.IsSupported) accessibility.Patterns.SelectionItem.Pattern.Select();
            else if (accessibility.Patterns.Invoke.IsSupported) accessibility.Patterns.Invoke.Pattern.Invoke();
            else throw new InvalidOperationException("Der Barrierefreiheitseintrag bietet kein Navigationsmuster.");
            actions.Add(accessibility.Properties.Name.ValueOrDefault ?? "Barrierefreiheit");
            Thread.Sleep(1000);
            SaveTree(automation, settings, Path.Combine(evidence, "contrast-accessibility.json"));
            Capture(settings, Path.Combine(evidence, "contrast-accessibility.png"));
            var contrast = settings.FindAllDescendants().FirstOrDefault(element => element.Patterns.Invoke.IsSupported &&
                element.Properties.Name.ValueOrDefault is { } name &&
                (name.StartsWith("Kontrastdesign", StringComparison.Ordinal) || name.StartsWith("Contrast themes", StringComparison.Ordinal)));
            if (contrast is null) throw new InvalidOperationException("Kein angebotener Kontrastdesign-Eintrag im tatsächlichen Settings-Baum.");
            actions.Add(contrast.Properties.Name.ValueOrDefault ?? "Kontrastdesigns");
            contrast.Patterns.Invoke.Pattern.Invoke();
            Thread.Sleep(1000);
            SaveTree(automation, settings, Path.Combine(evidence, "contrast-options.json"));
            Capture(settings, Path.Combine(evidence, "contrast-options.png"));
            var picker = settings.FindAllDescendants(condition => condition.ByControlType(ControlType.ComboBox))
                .FirstOrDefault(element => element.Properties.IsEnabled.ValueOrDefault && element.Patterns.ExpandCollapse.IsSupported);
            if (picker is null) throw new InvalidOperationException("Kein bedienbares Kontrastdesign-Auswahlfeld angeboten.");
            picker.Patterns.ExpandCollapse.Pattern.Expand();
            Thread.Sleep(500);
            SaveTree(automation, settings, Path.Combine(evidence, "contrast-offered-themes.json"));
            Capture(settings, Path.Combine(evidence, "contrast-offered-themes.png"));
            var choice = picker.AsComboBox().Items.FirstOrDefault(item => item.Properties.Name.ValueOrDefault is { Length: > 0 } name &&
                name is not ("Keine" or "Kein" or "Keins" or "None") && item.Patterns.SelectionItem.IsSupported);
            if (choice is null) throw new InvalidOperationException("Keine tatsächliche auswählbare Kontrastvorlage angeboten.");
            actions.Add(choice.Properties.Name.ValueOrDefault ?? "Kontrastvorlage");
            choice.Patterns.SelectionItem.Pattern.Select();
            picker.Patterns.ExpandCollapse.Pattern.Collapse();
            var apply = settings.FindAllDescendants().FirstOrDefault(element => element.Patterns.Invoke.IsSupported &&
                element.Properties.IsEnabled.ValueOrDefault && element.Properties.Name.ValueOrDefault is "Anwenden" or "Apply");
            if (apply is null) throw new InvalidOperationException("Kein bedienbarer Anwenden-Button für die tatsächliche Kontrastvorlage angeboten.");
            apply.Patterns.Invoke.Pattern.Invoke();
            actions.Add(apply.Properties.Name.ValueOrDefault ?? "Anwenden");
            Thread.Sleep(1000);
            SaveTree(automation, settings, Path.Combine(evidence, "contrast-after-apply.json"));
            Capture(settings, Path.Combine(evidence, "contrast-after-apply.png"));
            File.WriteAllText(Path.Combine(evidence, "contrast-attempt.json"), JsonSerializer.Serialize(new { Actions = actions, Error = (string?)null }));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidence, "contrast-attempt.json"), JsonSerializer.Serialize(new { Actions = actions, Error = error.Message }));
        }
    }

    private static void SaveTree(UIA3Automation automation, Window settings, string path)
    {
        var processId = settings.Properties.ProcessId.ValueOrDefault;
        var processIds = Process.GetProcessesByName("SystemSettings").Select(process => { using (process) return (uint)process.Id; })
            .Append(NativeProcessId(settings)).ToHashSet();
        var ownedWindows = automation.GetDesktop().FindAllChildren().Where(window => processIds.Contains(NativeProcessId(window)));
        var elements = ownedWindows.SelectMany(window => window.FindAllDescendants().Prepend(window)).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            ProcessId = processId, NativeProcessId = NativeProcessId(settings), WindowHandle = settings.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(),
            WindowTitle = settings.Title, Dpi = GetDpiForWindow(settings.Properties.NativeWindowHandle.ValueOrDefault),
            Elements = elements.Select(element => new
            {
                Id = element.Properties.AutomationId.ValueOrDefault, Name = element.Properties.Name.ValueOrDefault,
                Type = element.Properties.ControlType.ValueOrDefault.ToString(), IsEnabled = element.Properties.IsEnabled.ValueOrDefault,
                IsOffscreen = element.Properties.IsOffscreen.ValueOrDefault
            }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static uint NativeProcessId(AutomationElement element)
    { GetWindowThreadProcessId(element.Properties.NativeWindowHandle.ValueOrDefault, out var id); return id; }

    private static void Capture(Window settings, string path)
    {
        var handle = settings.Properties.NativeWindowHandle.ValueOrDefault;
        ShowWindow(handle, 9);
        SetForegroundWindow(handle);
        DwmFlush();
        Thread.Sleep(200);
        DwmFlush();
        if (!GetWindowRect(handle, out var rectangle)) throw new InvalidOperationException("Die eigene Gast-Anzeigeeinstellung hat keine lesbare Grenze.");
        using var bitmap = new Bitmap(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Die Anzeigeaufnahme ist fehlgeschlagen."); }
        finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out WindowRectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
