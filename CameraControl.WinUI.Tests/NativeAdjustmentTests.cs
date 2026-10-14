using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CameraControl.AppCore;
using CameraControl.AppCore.Storage;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("NativeAdjustment"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativeAdjustmentTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    [Test]
    public void SelectedPositionCanBeAdjustedAndSavedWithoutVideo()
    {
        Assert.That(Environment.UserName, Is.EqualTo("WDAGUtilityAccount").IgnoreCase);
        string evidence = Required("CAMERA_UI_EVIDENCE_DIRECTORY");
        string repo = Required("CAMERA_UI_REPO_ROOT");
        string cacheParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.AdjustmentQa"));
        string cache = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
        string data = Path.Combine(evidence, "adjust-selected", "qa-data");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(data);
        var processes = new List<int>();
        var screenshots = new List<string>();
        int port = 0;
        bool resizedWhileHeld = false;
        AppSettings? saved = null;
        try
        {
            using (var camera = new PhotoViscaFixture(repo, evidence, allowManualControl: true))
            {
                port = camera.Port;
                var defaults = AppSettings.CreateDefault();
                var before = defaults with
                {
                    Connection = new ConnectionSettings("127.0.0.1", camera.Port, null, null),
                    Presets = defaults.Presets.Select(value => value.Id switch
                    {
                        PresetIds.Leser => value with { Target = new CameraPosition(0, 0, 0) },
                        PresetIds.Tisch => value with { Target = new CameraPosition(1, -1, 4660) },
                        PresetIds.Redner => value with { Target = new CameraPosition(100, 200, 300) },
                        _ => value
                    }).ToArray()
                };
                new JsonSettingsRepository(data).SaveAsync(before).GetAwaiter().GetResult();
                Write(evidence, "settings-before.json", before);
                using (var session = new NativeQaSession("adjust-selected", "Light", bundleCache: cache))
                {
                    processes.Add(session.Application.ProcessId);
                    try
                    {
                        session.Resize(600, 430);
                        Navigate(session, "NavigateControl", "ControlPresetSelector");
                        Select(session, "Tisch", 1);
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { } readyPad &&
                            readyPad.Properties.IsEnabled.ValueOrDefault,
                            "Die eigene Loopback-Steuerung muss vor den UI-Bildern verbunden sein.");
                        Capture(session, "01-small-600x430", screenshots);

                        session.Resize(960, 560);
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetTisch")) is { } table && Visible(table) &&
                            session.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { } pad && Visible(pad),
                            "Das große Fenster muss Raster und Controller gleichzeitig zeigen.");
                        Assert.That(Find(session, "PresetTisch").Properties.BoundingRectangle.ValueOrDefault.Right,
                            Is.LessThanOrEqualTo(Find(session, "PanTiltPad").Properties.BoundingRectangle.ValueOrDefault.Left),
                            "Raster und Controller müssen getrennte nebeneinander liegende Flächen besitzen.");
                        AssertSelected(session, "Tisch");
                        Capture(session, "02-combined-960x560", screenshots);

                        session.Resize(360, 280);
                        AssertSelected(session, "Tisch");
                        Reach(session, "PanTiltPad");
                        Reach(session, "SaveSelectedPosition");
                        Capture(session, "03-minimum-360x280", screenshots);
                        Write(evidence, "early-screens-ready.json", new
                        {
                            Passed = true, Screenshots = screenshots.ToArray(), Count = screenshots.Count,
                            ExecutableVersion = FileVersionInfo.GetVersionInfo(Required("CAMERA_UI_EXE")).FileVersion,
                            ActualDpi = session.Dpi, NoMockups = true, FunctionalSaveCheckPending = true
                        });

                        session.Resize(600, 430);
                        Navigate(session, "NavigateSettings", "ConnectionHost");
                        var reachableSettings = new List<object>();
                        foreach (string id in new[] { "ConnectionHost", "ConnectionPort", "ConnectionRtsp", "ConnectionSave", "ConnectionCheck", "ConnectionReconnect" })
                        {
                            var element = Reach(session, id);
                            reachableSettings.Add(new { Id = id, Visible = Visible(element), Bounds = NativeQaSession.Rect(element) });
                        }
                        Capture(session, "04-settings-600x430", screenshots);
                        Write(evidence, "settings-reachability.json", reachableSettings);
                        Navigate(session, "NavigateControl", "ControlPresetSelector");
                        AssertSelected(session, "Tisch");
                        Invoke(session, "EditSelectedPhoto");
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetSelector")) is { } details && Visible(details),
                            "Foto… muss die Positionsdetails öffnen.");
                        Assert.That(Find(session, "PresetSelector").AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault,
                            Is.EqualTo("Tisch"), "Die geöffneten Positionsdetails müssen dieselbe Auswahl verwenden.");
                        var reachableDetails = new List<object>();
                        foreach (string id in new[] { "CapturePresetPhoto", "SavePreset" })
                        {
                            var settingsScroll = Find(session, "SettingsScroll");
                            Assert.That(settingsScroll.Patterns.Scroll.IsSupported, Is.True,
                                "Der tatsächliche Einstellungsbereich benötigt das native Scroll-Muster.");
                            NativeQaSession.WaitUntil(() =>
                            {
                                if (session.Window.FindFirstDescendant(c => c.ByAutomationId(id)) is { } button && Visible(button)) return true;
                                settingsScroll.Patterns.Scroll.Pattern.SetScrollPercent(-1, 100);
                                return false;
                            }, "Die native Scroll-Aktion muss den tatsächlichen Detailbutton sichtbar machen: " + id);
                            var element = Reach(session, id);
                            reachableDetails.Add(new { Id = id, Visible = Visible(element), Bounds = NativeQaSession.Rect(element) });
                        }
                        Capture(session, "04b-open-position-details", screenshots);
                        Write(evidence, "position-details-reachability.json", new { Selected = "Tisch", OpenedFromPhoto = true, Elements = reachableDetails });
                        Navigate(session, "NavigateControl", "ControlPresetSelector");
                        AssertSelected(session, "Tisch");

                        session.Resize(960, 560);
                        var pan = Reach(session, "PanTiltPad");
                        int priorMoves = camera.Count("pan-right");
                        int priorStops = camera.Count("pan-stop");
                        try
                        {
                            pan.Focus();
                            NativeQaSession.WaitUntil(() => pan.Properties.HasKeyboardFocus.ValueOrDefault, "Das echte Steuerfeld benötigt den Tastaturfokus.");
                            Keyboard.Press(VirtualKeyShort.RIGHT);
                            camera.WaitForCount("pan-right", priorMoves + 1);
                            Assert.That(IsHeld(VirtualKeyShort.RIGHT), Is.True);
                            session.Resize(600, 430);
                            camera.WaitForCount("pan-stop", priorStops + 1);
                            Assert.That(IsHeld(VirtualKeyShort.RIGHT), Is.True, "Der Resize muss stoppen, bevor der Test die gehaltene Taste loslässt.");
                            resizedWhileHeld = true;
                        }
                        finally { Keyboard.Release(VirtualKeyShort.RIGHT); }
                        AssertSelected(session, "Tisch");
                        var save = Reach(session, "SaveSelectedPosition");
                        NativeQaSession.WaitUntil(() => save.Properties.IsEnabled.ValueOrDefault, "Speichern benötigt den bestätigten Stopp und die bestehende Verbindung.");
                        int panReads = camera.Count("pan-query");
                        int zoomReads = camera.Count("zoom-query");
                        Invoke(session, "SaveSelectedPosition");
                        camera.WaitForCount("pan-query", panReads + 1);
                        camera.WaitForCount("zoom-query", zoomReads + 1);
                        NativeQaSession.WaitUntil(() => (saved = ReadSettings(data)).Presets.Single(value => value.Id == PresetIds.Tisch).Target == new CameraPosition(-1, 1, 4660),
                            "Position speichern muss die gelesenen Werte ausschließlich unter Tisch übernehmen.");
                        AssertOnlyTischChanged(before, saved!);
                        AssertNoPreview(session);
                        Capture(session, "05-saved-tisch", screenshots);
                        Write(evidence, "settings-after-save.json", saved);

                        var homeSelectionStarted = DateTime.UtcNow;
                        Select(session, "Home", 3);
                        try
                        {
                            NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("SaveSelectedPosition")) is { } homeSave &&
                                !homeSave.Properties.IsEnabled.ValueOrDefault,
                                "Home darf keine lokale Zielposition speichern.");
                        }
                        catch (AssertionException)
                        {
                            Capture(session, "home-controller-state", screenshots);
                            var observed = DateTime.UtcNow;
                            var controllerSelection = Find(session, "ControlPresetSelector").AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault;
                            Invoke(session, "EditSelectedPhoto");
                            NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetSelector")) is { } details && Visible(details),
                                "Der Diagnoseweg muss die tatsächlichen Positionsdetails öffnen.");
                            var detailsSelection = Find(session, "PresetSelector").AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault;
                            Write(evidence, "home-selection-observation.json", new { SelectionStartedUtc = homeSelectionStarted,
                                ObservedUtc = observed, ControllerSelection = controllerSelection, SaveEnabledAfterWait = true,
                                DetailsSelection = detailsSelection, DetailsObservedUtc = DateTime.UtcNow, WaitLimitSeconds = 10 });
                            Capture(session, "home-details-state", screenshots);
                            throw;
                        }
                        Assert.That(ReadSettings(data).Presets.Single(value => value.Id == PresetIds.Home).Target, Is.Null);
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("SaveSelectedPositionHint")) is { } hint &&
                            Visible(hint) && hint.Properties.Name.ValueOrDefault == "Home hat keinen lokalen Zielwert.",
                            "Der sichtbare Home-Sperrgrund muss die deaktivierte Aktion erklären.");
                        Capture(session, "06-home-save-disabled", screenshots);
                        session.Resize(960, 560);
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetHome")) is { } tile && Visible(tile),
                            "Das breite Fenster muss das tatsächliche Positionsraster wieder zeigen.");
                        AssertSelected(session, "Home");
                        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetHome")) is { } homeTile &&
                            homeTile.Properties.HelpText.ValueOrDefault == "Gewählte Position" &&
                            session.Window.FindFirstDescendant(c => c.ByAutomationId("PresetTisch")) is { } tableTile &&
                            tableTile.Properties.HelpText.ValueOrDefault != "Gewählte Position",
                            "Positionsraster und Controller müssen nach dem Größenwechsel gemeinsam Home auswählen.");
                        Assert.That(Find(session, "SaveSelectedPosition").Properties.IsEnabled.ValueOrDefault, Is.False);
                        Write(evidence, "home-both-views.json", new { Controller = "Home",
                            HomeHelpText = Find(session, "PresetHome").Properties.HelpText.ValueOrDefault,
                            TischHelpText = Find(session, "PresetTisch").Properties.HelpText.ValueOrDefault, SaveEnabled = false });
                        Capture(session, "06b-home-both-views", screenshots);
                        camera.AssertHealthy();
                    }
                    catch
                    {
                        try { session.Capture("failure"); }
                        catch (Exception diagnostic) { File.WriteAllText(Path.Combine(evidence, "capture-error.txt"), diagnostic.ToString()); }
                        throw;
                    }
                }

                using (var restarted = new NativeQaSession("adjust-selected", "Light", bundleCache: cache))
                {
                    processes.Add(restarted.Application.ProcessId);
                    restarted.Resize(600, 430);
                    AssertOnlyTischChanged(before, ReadSettings(data));
                    Navigate(restarted, "NavigateControl", "ControlPresetSelector");
                    Select(restarted, "Tisch", 1);
                    Assert.That(ReadSettings(data).Presets, Is.EqualTo(saved!.Presets));
                    AssertNoPreview(restarted);
                    Capture(restarted, "07-restart-saved-tisch", screenshots);
                    camera.AssertHealthy();
                }
            }
            Assert.That(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port && System.Net.IPAddress.IsLoopback(endpoint.Address)), Is.False);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.RIGHT);
            Mouse.Up(MouseButton.Left);
            var remaining = processes.Where(IsRunning).ToArray();
            bool removed = false;
            var full = Path.GetFullPath(cache);
            Assert.That(full.StartsWith(cacheParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(Path.GetFileName(full), "N", out _), Is.True);
            if (remaining.Length == 0 && Directory.Exists(full))
            {
                Assert.That(File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint), Is.False);
                Directory.Delete(full, recursive: true);
                removed = !Directory.Exists(full);
            }
            Write(evidence, "adjustment-cleanup.json", new { ProcessIds = processes, RemainingOwnProcessIds = remaining,
                Cache = full, CacheRemoved = removed, ViscaPort = port, KillUsed = false, HostInputs = false });
            Assert.That(remaining, Is.Empty);
        }
        Write(evidence, "adjustment-result.json", new
        {
            Passed = true, SelectedId = PresetIds.Tisch, SavedTarget = new CameraPosition(-1, 1, 4660),
            OtherPresetsPreserved = true, SelectionPreservedOnResizeAndNavigation = true, RestartVerified = true,
            ResizeStoppedWhileKeyHeld = resizedWhileHeld, HomeLocalSaveDisabled = true,
            RtspConfigured = false, RtspServerStarted = false, PreviewOpened = false,
            Screenshots = screenshots, Source = "Actual native input and fixed own loopback VISCA responses; no physical camera"
        });
    }

    private static void AssertOnlyTischChanged(AppSettings before, AppSettings after)
    {
        Assert.That(after.Presets.Select(value => value.Id), Is.EqualTo(before.Presets.Select(value => value.Id)));
        foreach (var preset in before.Presets)
        {
            var actual = after.Presets.Single(value => value.Id == preset.Id);
            Assert.That(actual, Is.EqualTo(preset.Id == PresetIds.Tisch ? preset with { Target = new CameraPosition(-1, 1, 4660) } : preset));
        }
        Assert.That(after.Connection.RtspUrl, Is.Null);
    }

    private static void Navigate(NativeQaSession session, string id, string anchor)
    {
        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId(id)) is { } item && Visible(item),
            "Die Navigation muss nach dem Größenwechsel wieder sichtbar im nativen Baum stehen: " + id);
        var navigation = Find(session, id);
        Assert.That(navigation.Patterns.SelectionItem.IsSupported, Is.True,
            "Der beobachtete native Tab benötigt das SelectionItem-Muster: " + id);
        navigation.Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => navigation.Patterns.SelectionItem.Pattern.IsSelected.Value,
            "Der angeforderte native Tab muss die Auswahl bestätigen: " + id);
        Write(session.EvidenceDirectory, "navigation-" + id + "-" + Guid.NewGuid().ToString("N") + ".json", new
        {
            RequestedId = id, ActualId = navigation.Properties.AutomationId.ValueOrDefault,
            Name = navigation.Properties.Name.ValueOrDefault, Type = navigation.Properties.ControlType.ValueOrDefault.ToString(),
            SelectionItemSupported = true, Selected = navigation.Patterns.SelectionItem.Pattern.IsSelected.Value,
            SelectedUtc = DateTime.UtcNow, RequiredContentAnchor = anchor, CoordinateClickUsed = false
        });
        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId(anchor)) is not null,
            "Die angeforderte native Seite fehlt: " + anchor);
    }

    private static void Select(NativeQaSession session, string name, int index)
    {
        var combo = Reach(session, "ControlPresetSelector");
        NativeQaSession.WaitUntil(() => combo.Properties.IsEnabled.ValueOrDefault, "Die Positionsauswahl ist noch gesperrt.");
        combo.AsComboBox().Select(index);
        if (combo.Patterns.ExpandCollapse.IsSupported) combo.Patterns.ExpandCollapse.Pattern.Collapse();
        AssertSelected(session, name);
        var recall = Reach(session, "RecallSelectedPosition");
        NativeQaSession.WaitUntil(() => recall.Properties.HasKeyboardFocus.ValueOrDefault,
            "Die Positionsauswahl muss vor dem Bildnachweis abgeschlossen sein.");
        AssertSelected(session, name);
    }

    private static void AssertSelected(NativeQaSession session, string name) => NativeQaSession.WaitUntil(() =>
        session.Window.FindFirstDescendant(c => c.ByAutomationId("ControlPresetSelector")) is { } selector &&
        selector.AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault == name,
        "Die ausgewählte feste Position muss erhalten bleiben: " + name);

    private static AutomationElement Find(NativeQaSession session, string id) => session.Window.FindFirstDescendant(c => c.ByAutomationId(id)) ??
        throw new AssertionException("Das erforderliche native Element fehlt: " + id);

    private static AutomationElement Reach(NativeQaSession session, string id)
    {
        AutomationElement? element = null;
        NativeQaSession.WaitUntil(() => (element = session.Window.FindFirstDescendant(c => c.ByAutomationId(id))) is not null,
            "Das erforderliche native Element muss nach dem Layoutwechsel im Baum stehen: " + id);
        var present = element!;
        if (present.Patterns.ScrollItem.IsSupported) present.Patterns.ScrollItem.Pattern.ScrollIntoView();
        if (present.Properties.IsEnabled.ValueOrDefault) present.Focus();
        NativeQaSession.WaitUntil(() => Visible(present), "Das erforderliche Element muss in der eigenen Oberfläche erreichbar sein: " + id);
        return present;
    }

    private static void Invoke(NativeQaSession session, string id)
    {
        var element = Reach(session, id);
        Assert.That(element.Properties.IsEnabled.ValueOrDefault, Is.True, "Die native Aktion muss freigegeben sein: " + id);
        Assert.That(element.Patterns.Invoke.IsSupported, Is.True, "Die beobachtete Schaltfläche benötigt das Invoke-Muster: " + id);
        Write(session.EvidenceDirectory, "action-" + id + "-" + Guid.NewGuid().ToString("N") + ".json", new
        {
            ActualId = element.Properties.AutomationId.ValueOrDefault, Name = element.Properties.Name.ValueOrDefault,
            InvokeSupported = true, IsEnabled = true, Bounds = NativeQaSession.Rect(element), ObservedUtc = DateTime.UtcNow
        });
        element.Patterns.Invoke.Pattern.Invoke();
    }

    private static bool Visible(AutomationElement element)
    {
        var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        return !element.Properties.IsOffscreen.ValueOrDefault && bounds.Width > 0 && bounds.Height > 0;
    }

    private static void Capture(NativeQaSession session, string name, List<string> screenshots)
    {
        nint handle = session.Window.Properties.NativeWindowHandle.ValueOrDefault;
        GetWindowThreadProcessId(handle, out uint processId);
        Assert.That(processId, Is.EqualTo((uint)session.Application.ProcessId));
        session.Window.Focus();
        NativeQaSession.WaitUntil(() => GetForegroundWindow() == handle, "Das eigene Fenster muss für den Bildnachweis im Vordergrund stehen.");
        var wait = Stopwatch.StartNew();
        var rejected = new List<object>();
        while (true)
        {
            try { session.Capture(name); break; }
            catch (AssertionException error) when (error.Message.Contains("Eine schwarze oder leere Aufnahme ist kein UI-Nachweis.", StringComparison.Ordinal))
            {
                string original = Path.Combine(session.EvidenceDirectory, $"{session.Dpi}dpi-Light-{name}.png");
                string savedAttempt = Path.Combine(session.EvidenceDirectory, $"{session.Dpi}dpi-Light-{name}-rejected-{rejected.Count + 1}.png");
                File.Copy(original, savedAttempt, overwrite: false);
                rejected.Add(new { Screenshot = savedAttempt, ObservedUtc = DateTime.UtcNow, Milliseconds = wait.ElapsedMilliseconds });
                Write(session.EvidenceDirectory, name + "-capture-readiness.json", new { LimitSeconds = 10, Rejected = rejected, Passed = false });
                if (wait.Elapsed >= TimeSpan.FromSeconds(10)) throw;
                Thread.Sleep(100);
            }
        }
        Write(session.EvidenceDirectory, name + "-capture-readiness.json", new { LimitSeconds = 10, Rejected = rejected, Passed = true });
        screenshots.Add(Path.Combine(session.EvidenceDirectory, $"{session.Dpi}dpi-Light-{name}.png"));
    }

    private static void AssertNoPreview(NativeQaSession session)
    {
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != session.Application.ProcessId || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.ToString().StartsWith("Vorschau", StringComparison.Ordinal)) windows.Add(window);
            return true;
        }, 0);
        Assert.That(windows, Is.Empty, "Position speichern benötigt kein Vorschaufenster.");
    }

    private static bool IsRunning(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static AppSettings ReadSettings(string data)
    {
        using var stream = new FileStream(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<AppSettings>(stream, Json) ?? throw new InvalidDataException("Die eigene QA-Einstellungsdatei ist leer.");
    }
    private static string Required(string name) => Path.GetFullPath(Environment.GetEnvironmentVariable(name) ?? throw new AssertionException(name + " fehlt."));
    private static void Write(string evidence, string name, object? value)
    {
        var target = Path.Combine(evidence, name);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, target, overwrite: true);
    }
    private static bool IsHeld(VirtualKeyShort key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
}
