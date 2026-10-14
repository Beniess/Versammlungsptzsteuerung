using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

internal static class CleanWindowsProbe
{
    internal const string AppName = "Versammlungs-Kamerasteuerung";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static void Run(string executable, string evidence)
    {
        GuardGuest(executable, evidence);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installation = Path.Combine(local, "Programs", "Versammlungsptzsteuerung");
        var data = Path.Combine(local, "Versammlungsptzsteuerung", "Data");
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
        Require(!Directory.Exists(installation) && !Directory.Exists(data) && !File.Exists(link), "Der Gast enthält bereits eigene Produktdaten oder eine Programmeinrichtung.");
        Directory.CreateDirectory(evidence);
        var expectedHash = Hash(executable);
        var sourceVersion = FileVersionInfo.GetVersionInfo(executable).FileVersion!;
        var version = Version.Parse(sourceVersion);
        var releaseVersion = version.Revision == 0 ? version.ToString(3) : version.ToString(4);
        var installed = Path.Combine(installation, "Versions", releaseVersion, "CameraControl.WinUI.exe");
        var download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CameraControl.WinUI (1).exe");
        var scenarios = new List<object>();
        var sourceProcesses = new List<Process>();
        string? failure = null;
        string? fastSaveFailure = null;
        object cleanup = new { RemainingOwnProcessIds = Array.Empty<int>() };
        using var automation = new UIA3Automation();
        try
        {
            var firstWatch = Stopwatch.StartNew();
            sourceProcesses.Add(StartNormal(executable));
            WaitUntil(() => MarkerMatches(installation, installed, releaseVersion, expectedHash), "Der echte aktive Marker fehlt.", 120);
            var first = WaitInstalled(automation, installed);
            WaitSourceExit(sourceProcesses[^1]);
            VerifyInstalled(first, installed, expectedHash, sourceVersion);
            var linkInfo = ReadLink(link);
            Require(Same(linkInfo.Target, installed) && linkInfo.Arguments.Length == 0 && linkInfo.AppUserModelId == "Versammlungsptzsteuerung.CameraControl", "Der echte Startmenülink enthält nicht das installierte Ziel und die Produkt-AUMID.");
            Write(evidence, "start-menu-link.json", new { Path = link, linkInfo.Target, linkInfo.Arguments, linkInfo.AppUserModelId, Sha256 = Hash(link) });
            CleanWindowsSurface.Capture(first.Window, Path.Combine(evidence, "first-installed-window.png"));
            CloseInstalled(first, installed);
            scenarios.Add(new { Name = "ReadOnlyPayloadInstallsNormalRelease", Passed = true, first.ProcessId, Installed = installed, Milliseconds = firstWatch.ElapsedMilliseconds, Source = executable, SourceReadOnly = true });

            Require(!File.Exists(download), "Die eigene Download-Testdatei existiert bereits.");
            Directory.CreateDirectory(Path.GetDirectoryName(download)!);
            CopyVerified(executable, download);
            sourceProcesses.Add(StartNormal(download));
            var fromDownload = WaitInstalled(automation, installed);
            WaitSourceExit(sourceProcesses[^1]);
            VerifyInstalled(fromDownload, installed, expectedHash, sourceVersion);
            CloseInstalled(fromDownload, installed);
            CleanWindowsSurface.SearchAndLaunch(automation, evidence, "search-before-download-removal");
            var fromSearch = WaitInstalled(automation, installed);
            VerifyInstalled(fromSearch, installed, expectedHash, sourceVersion);
            Require(fromSearch.ProcessId != fromDownload.ProcessId, "Der Suchstart muss nach dem beendeten Start einen neuen Prozess erzeugen.");
            CleanWindowsSurface.Capture(fromSearch.Window, Path.Combine(evidence, "search-started-window.png"));
            CloseInstalled(fromSearch, installed);
            scenarios.Add(new { Name = "ActualWindowsSearchStartsInstalledRelease", Passed = true, fromSearch.ProcessId, Installed = installed, Download = download });

            var dataPath = Path.Combine(data, "settings.json");
            var settingsBefore = File.ReadAllText(dataPath);
            File.Delete(download);
            Require(!File.Exists(download) && File.Exists(installed) && File.Exists(link), "Download-Löschung hat nicht den erwarteten Dateistand erzeugt.");
            CleanWindowsSurface.SearchAndLaunch(automation, evidence, "search-after-download-removal");
            var withoutDownload = WaitInstalled(automation, installed);
            VerifyInstalled(withoutDownload, installed, expectedHash, sourceVersion);
            Require(MarkerMatches(installation, installed, releaseVersion, expectedHash), "Der aktive Marker hat sich beim Suchstart geändert.");
            Require(File.ReadAllText(dataPath) == settingsBefore, "Der Start hat die gespeicherten Daten geändert.");
            CleanWindowsSurface.Capture(withoutDownload.Window, Path.Combine(evidence, "without-download-window.png"));
            scenarios.Add(new { Name = "ActualWindowsSearchWorksWithoutDownload", Passed = true, withoutDownload.ProcessId, Installed = installed, DownloadRemoved = !File.Exists(download), SettingsPreservedAtStartup = true });

            using (var media = new CleanWindowsMedia(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(executable))!, "Media"), evidence))
            {
                withoutDownload.Window.FindFirstDescendant(c => c.ByAutomationId("NavigateSettings"))!.Patterns.SelectionItem.Pattern.Select();
                fastSaveFailure = ObserveFastConnectionSave(withoutDownload.Window, data, media.Address, evidence);
                CleanWindowsSurface.EnterText(withoutDownload.Window, WaitElement(withoutDownload.Window, "ConnectionHost"), "127.0.0.1");
                CleanWindowsSurface.EnterText(withoutDownload.Window, WaitElement(withoutDownload.Window, "ConnectionRtsp"), media.Address);
                var save = WaitElement(withoutDownload.Window, "ConnectionSave");
                save.Focus();
                if (save.Patterns.ScrollItem.IsSupported) save.Patterns.ScrollItem.Pattern.ScrollIntoView();
                WaitUntil(() => CleanWindowsSurface.IsVisible(save) && save.Properties.IsEnabled.ValueOrDefault, "Die sichtbare Speichern-Aktion ist nicht bereit.", 15);
                var visibleHost = WaitElement(withoutDownload.Window, "ConnectionHost").AsTextBox().Text;
                var visibleRtsp = WaitElement(withoutDownload.Window, "ConnectionRtsp").AsTextBox().Text;
                Write(evidence, "connection-before-save.json", new { Host = visibleHost, Rtsp = visibleRtsp, ExpectedRtsp = media.Address,
                    SaveVisible = CleanWindowsSurface.IsVisible(save), SaveEnabled = save.Properties.IsEnabled.ValueOrDefault, NativeKeyboardInput = true });
                Require(visibleHost == "127.0.0.1" && visibleRtsp == media.Address, "Die eingegebenen Verbindungsfelder wurden vor dem Speichern verändert.");
                CleanWindowsSurface.SaveTree(withoutDownload.Window, Path.Combine(evidence, "connection-before-save-tree.json"));
                CleanWindowsSurface.Capture(withoutDownload.Window, Path.Combine(evidence, "connection-before-save.png"), fromScreen: true);
                save.Click();
                WaitUntil(() => File.Exists(Path.Combine(data, "settings.json")) && File.ReadAllText(Path.Combine(data, "settings.json")).Contains(media.Address, StringComparison.Ordinal), "Die echten Verbindungseinstellungen wurden nicht gespeichert.", 15);
                WaitElement(withoutDownload.Window, "OpenPreview").Patterns.Invoke.Pattern.Invoke();
                var preview = WaitWindow(automation, withoutDownload.ProcessId, "PreviewReconnect", 30);
                WaitUntil(() => preview.FindFirstDescendant(c => c.ByAutomationId("PreviewStatus"))?
                    .FindFirstDescendant(c => c.ByAutomationId("Message"))?.Properties.Name.ValueOrDefault?
                    .Contains("Das Livebild ist verfügbar.", StringComparison.Ordinal) == true, "Die echte Vorschau meldet kein Livebild.", 15);
                media.WaitReader();
                VerifyBundle(withoutDownload.ProcessId, installed, expectedHash, evidence);
                CleanWindowsSurface.Capture(preview, Path.Combine(evidence, "clean-windows-preview.png"));
                CleanWindowsSurface.SaveTree(preview, Path.Combine(evidence, "clean-windows-preview.json"));
                preview.Close();
                WaitUntil(() => !CleanWindowsSurface.OwnVisibleWindows(withoutDownload.ProcessId).Any(handle =>
                    automation.FromHandle(handle).FindFirstDescendant(c => c.ByAutomationId("PreviewReconnect")) is not null), "Das Vorschaufenster wurde nicht geschlossen.", 15);
                media.WaitNoReader();
            }
            CloseInstalled(withoutDownload, installed);
            scenarios.Add(new { Name = "PrivateRuntimeAndLoopbackVideo", Passed = true, withoutDownload.ProcessId, Installed = installed, StaticCoreClrHost = true });
        }
        catch (Exception error)
        {
            failure = error.ToString();
            CleanWindowsSurface.SaveDesktopEvidence(automation, evidence, "failure");
        }
        finally
        {
            foreach (var process in OwnProcesses(installed))
            {
                using (process)
                {
                    foreach (var handle in CleanWindowsSurface.OwnVisibleWindows(process.Id)) CleanWindowsSurface.RequestClose(handle);
                    process.WaitForExit(15000);
                }
            }
            foreach (var process in sourceProcesses.Where(process => !process.HasExited))
            {
                var path = process.MainModule?.FileName;
                Require(Same(path, executable) || Same(path, download), "Ein zu schließender Quellprozess gehört nicht zum eigenen Test.");
                foreach (var handle in CleanWindowsSurface.OwnVisibleWindows(process.Id)) CleanWindowsSurface.RequestClose(handle);
                process.WaitForExit(15000);
            }
            var remaining = OwnProcesses(installed).Select(process => { using (process) return process.Id; }).ToArray();
            var remainingSources = sourceProcesses.Where(process => !process.HasExited).Select(process => process.Id).ToArray();
            foreach (var process in sourceProcesses) process.Dispose();
            cleanup = new { RemainingOwnProcessIds = remaining.Concat(remainingSources).ToArray(), KillUsed = false, GuestShutdownRemovesInstallation = true };
            if (remaining.Length + remainingSources.Length != 0) failure ??= "Eigene Programmprozesse sind nach WM_CLOSE noch aktiv.";
            foreach (var source in new[] { Path.Combine(installation, "active.json"), Path.Combine(installation, "transaction.json"), Path.Combine(data, "settings.json") })
                if (File.Exists(source)) File.WriteAllBytes(Path.Combine(evidence, Path.GetFileName(source)), File.ReadAllBytes(source));
            failure ??= fastSaveFailure;
            Write(evidence, "clean-windows-results.json", new { Passed = failure is null && scenarios.Count == 4, Scenarios = scenarios, Error = failure, Cleanup = cleanup,
                SourceSha256 = expectedHash, SourceFileVersion = sourceVersion, InstalledPath = installed, DataPath = data, LinkPath = link, QaMode = false });
        }
        if (failure is not null) throw new InvalidOperationException(failure);
    }

    private static string? ObserveFastConnectionSave(Window window, string data, string address, string evidence)
    {
        WaitElement(window, "ConnectionHost").AsTextBox().Text = "127.0.0.1";
        WaitElement(window, "ConnectionRtsp").AsTextBox().Text = address;
        var hostBefore = WaitElement(window, "ConnectionHost").AsTextBox().Text;
        var rtspBefore = WaitElement(window, "ConnectionRtsp").AsTextBox().Text;
        var established = hostBefore == "127.0.0.1" && rtspBefore == address;
        Write(evidence, "fast-save-before.json", new { Host = hostBefore, Rtsp = rtspBefore, ExpectedRtsp = address, FieldsEstablished = established });
        WaitElement(window, "ConnectionSave").Patterns.Invoke.Pattern.Invoke();
        var saved = false;
        try
        {
            WaitUntil(() => File.Exists(Path.Combine(data, "settings.json")) && File.ReadAllText(Path.Combine(data, "settings.json")).Contains(address, StringComparison.Ordinal),
                "Der schnelle Speichervorgang hat die eingegebene RTSP-Adresse nicht erhalten.", 15);
            saved = true;
        }
        catch (TimeoutException) { }
        var path = Path.Combine(data, "settings.json");
        if (File.Exists(path)) File.Copy(path, Path.Combine(evidence, "fast-save-settings.json"));
        var productFailure = established && !saved;
        Write(evidence, "fast-save-regression.json", new { FieldsEstablished = established, SavedExpectedValue = saved, ProductLostVisibleValue = productFailure,
            HostBefore = hostBefore, RtspBefore = rtspBefore, RtspAfter = WaitElement(window, "ConnectionRtsp").AsTextBox().Text, ExpectedRtsp = address,
            ExplicitWaitBeforeSave = false, SaveDeadlineSeconds = 15 });
        return productFailure ? "Der schnelle Speichervorgang hat eine zuvor sichtbare RTSP-Adresse verloren." : null;
    }

    private static void GuardGuest(string executable, string evidence)
    {
        Require(Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase), "Clean-Windows darf nur im eigenen Sandbox-Gast laufen.");
        Require(Same(Path.GetFullPath(executable), @"C:\Payload\App\CameraControl.WinUI.exe") && Same(Path.GetFullPath(evidence), @"C:\Evidence\Probe"), "Die geprüften Gastpfade stimmen nicht überein.");
        using var document = JsonDocument.Parse(File.ReadAllText(@"C:\Evidence\guest-environment.json"));
        var environment = document.RootElement;
        Require(environment.GetProperty("ComputerModel").GetString() == "Virtual Machine" && environment.GetProperty("CleanModernRuntimeEnvironment").GetBoolean(), "Der belegte saubere Gast fehlt.");
    }

    private static Process StartNormal(string path)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("CAMERA_CONTROL_QA_", StringComparison.Ordinal) || key.StartsWith("CAMERA_UI_", StringComparison.Ordinal) || key == "DOTNET_BUNDLE_EXTRACT_BASE_DIR").ToArray()) start.Environment.Remove(key);
        return Process.Start(start) ?? throw new IOException("Windows hat keinen Produktprozess erzeugt.");
    }

    private static void WaitSourceExit(Process process)
    {
        Require(process.WaitForExit(30000), "Der Quellprozess hat sich nach der Übergabe nicht beendet.");
        Require(process.ExitCode == 0, "Der Quellprozess meldet einen Fehler.");
    }

    private static InstalledWindow WaitInstalled(UIA3Automation automation, string installed)
    {
        InstalledWindow? result = null;
        WaitUntil(() =>
        {
            var matches = OwnProcesses(installed);
            try
            {
                Require(matches.Length <= 1, "Mehrere installierte Produktprozesse laufen gleichzeitig.");
                if (matches.Length == 0) return false;
                foreach (var handle in CleanWindowsSurface.OwnVisibleWindows(matches[0].Id))
                {
                    var window = automation.FromHandle(handle).AsWindow();
                    if (window.FindFirstDescendant(c => c.ByAutomationId("OpenPreview"))?.Properties.IsEnabled.ValueOrDefault != true) continue;
                    Require(CleanWindowsSurface.Responds(handle), "Das sichtbare Produktfenster antwortet nicht auf WM_NULL.");
                    result = new InstalledWindow(matches[0].Id, window);
                    return true;
                }
                return false;
            }
            finally { foreach (var process in matches) process.Dispose(); }
        }, "Der installierte Prozess hat kein sichtbares und freigegebenes Hauptfenster.", 60);
        return result!;
    }

    private static Window WaitWindow(UIA3Automation automation, int processId, string anchor, int seconds)
    {
        Window? found = null;
        WaitUntil(() =>
        {
            found = CleanWindowsSurface.OwnVisibleWindows(processId).Select(handle => automation.FromHandle(handle).AsWindow())
                .FirstOrDefault(window => window.FindFirstDescendant(c => c.ByAutomationId(anchor)) is not null);
            return found is not null;
        }, "Das eigene Fenster fehlt: " + anchor, seconds);
        return found!;
    }

    private static AutomationElement WaitElement(Window window, string id)
    {
        AutomationElement? element = null;
        WaitUntil(() => (element = window.FindFirstDescendant(c => c.ByAutomationId(id)))?.Properties.IsEnabled.ValueOrDefault == true, "Das Feld ist nicht bereit: " + id, 15);
        return element!;
    }

    private static void CloseInstalled(InstalledWindow installedWindow, string path)
    {
        using var process = Process.GetProcessById(installedWindow.ProcessId);
        Require(Same(process.MainModule?.FileName, path), "Der zu schließende Prozess gehört nicht zum Test.");
        _ = process.SafeHandle;
        installedWindow.Window.Close();
        Require(process.WaitForExit(15000) && process.ExitCode == 0, "Das Produkt wurde nicht fehlerfrei und geordnet beendet.");
    }

    private static void VerifyInstalled(InstalledWindow window, string path, string hash, string version)
    {
        using var process = Process.GetProcessById(window.ProcessId);
        Require(Same(process.MainModule?.FileName, path) && Hash(path) == hash && FileVersionInfo.GetVersionInfo(path).FileVersion == version, "Prozesspfad, SHA256 oder Dateiversion weichen von der Quelle ab.");
    }

    private static bool MarkerMatches(string root, string executable, string version, string hash)
    {
        var path = Path.Combine(root, "active.json");
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var marker = document.RootElement;
        var release = marker.GetProperty("release");
        return marker.GetProperty("schemaVersion").GetInt32() == 1 && Same(release.GetProperty("executablePath").GetString(), executable) &&
            release.GetProperty("version").GetString() == version && string.Equals(release.GetProperty("sha256").GetString(), hash, StringComparison.OrdinalIgnoreCase) &&
            release.GetProperty("length").GetInt64() == new FileInfo(executable).Length;
    }

    private static void VerifyBundle(int processId, string installed, string expectedHash, string evidence)
    {
        using var process = Process.GetProcessById(processId);
        var host = process.MainModule?.FileName;
        Require(Same(host, installed) && Hash(installed) == expectedHash, "Der CLR-Host ist nicht die geprüfte installierte EXE.");
        using var file = File.OpenRead(installed);
        using var pe = new System.Reflection.PortableExecutable.PEReader(file);
        var exportDirectory = pe.PEHeaders.PEHeader!.ExportTableDirectory;
        Require(exportDirectory.RelativeVirtualAddress != 0 && exportDirectory.Size >= 40, "Der Single-File-Host besitzt keine Runtime-Exporttabelle.");
        var header = pe.GetSectionData(exportDirectory.RelativeVirtualAddress).GetContent(0, 40).ToArray();
        var count = BitConverter.ToInt32(header, 24);
        Require(count is > 0 and < 4096, "Die Runtime-Exporttabelle hat eine ungültige Größe.");
        var addresses = pe.GetSectionData(BitConverter.ToInt32(header, 32)).GetContent(0, count * 4).ToArray();
        var exports = Enumerable.Range(0, count).Select(index => System.Text.Encoding.ASCII.GetString(
            pe.GetSectionData(BitConverter.ToInt32(addresses, index * 4)).GetContent().TakeWhile(value => value != 0).ToArray())).ToArray();
        var requiredExports = new[] { "DotNetRuntimeInfo", "g_dacTable", "MetaDataGetDispenser" };
        Require(requiredExports.All(exports.Contains), "Die geprüfte EXE enthält nicht die erwarteten statischen CoreCLR-Exporte.");
        var names = new[] { "Microsoft.UI.Xaml.dll", "libvlc.dll", "libvlccore.dll" };
        var modules = process.Modules.Cast<ProcessModule>().Where(module => names.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
            .Select(module => new { Name = module.ModuleName, Path = module.FileName, Version = FileVersionInfo.GetVersionInfo(module.FileName).FileVersion, Sha256 = Hash(module.FileName) }).ToArray();
        Write(evidence, "loaded-bundle-modules.json", new { ProcessId = processId, HostPath = host, HostSha256 = expectedHash,
            StaticCoreClr = true, VerifiedRuntimeExports = requiredExports, Modules = modules });
        Require(modules.Length == names.Length && names.All(name => modules.Count(module => name.Equals(module.Name, StringComparison.OrdinalIgnoreCase)) == 1), "Nicht alle drei privaten WinUI-/Video-Pflichtmodule sind im Produktprozess geladen.");
        var root = Path.GetDirectoryName(modules.Single(module => module.Name.Equals("Microsoft.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase)).Path)!;
        var extractionParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ".net", "CameraControl.WinUI")) + Path.DirectorySeparatorChar;
        Require(root.StartsWith(extractionParent, StringComparison.OrdinalIgnoreCase) && modules.All(module => module.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "Die Pflichtmodule stammen nicht aus demselben eigenen Single-EXE-Bundle.");
    }

    private static LinkInfo ReadLink(string path)
    {
        Require(File.Exists(path), "Der echte Startmenülink fehlt.");
        object? shell = null, shortcut = null, explorer = null, folder = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            explorer = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            folder = ((dynamic)explorer!).NameSpace(Path.GetDirectoryName(path));
            item = ((dynamic)folder!).ParseName(Path.GetFileName(path));
            return new LinkInfo((string)((dynamic)shortcut).TargetPath, (string)((dynamic)shortcut).Arguments, Convert.ToString(((dynamic)item!).ExtendedProperty("System.AppUserModel.ID")) ?? "");
        }
        finally { foreach (var value in new[] { item, folder, explorer, shortcut, shell }) if (value is not null) Marshal.FinalReleaseComObject(value); }
    }

    private static Process[] OwnProcesses(string path)
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            try { if (Same(process.MainModule?.FileName, path)) { found.Add(process); continue; } }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        return found.ToArray();
    }

    private static void CopyVerified(string source, string target)
    {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
        Require(Hash(source) == Hash(target) && FileVersionInfo.GetVersionInfo(source).FileVersion == FileVersionInfo.GetVersionInfo(target).FileVersion, "Die Downloadkopie ist beschädigt.");
    }

    internal static void WaitUntil(Func<bool> condition, string failure, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds)) { if (condition()) return; Thread.Sleep(100); }
        throw new TimeoutException(failure);
    }

    internal static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
    internal static void Write(string evidence, string name, object value) => File.WriteAllText(Path.Combine(evidence, name), JsonSerializer.Serialize(value, Json));
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static bool Same(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private sealed record InstalledWindow(int ProcessId, Window Window);
    private sealed record LinkInfo(string Target, string Arguments, string AppUserModelId);
}
