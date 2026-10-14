using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CameraControl.AppCore;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("PhotosAndPreview"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativePreviewTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    [Test]
    public void ThreeRealTestPhotosAppearOnPositionsAndSurviveRestart()
    {
        string repo = Required("CAMERA_UI_REPO_ROOT");
        string evidence = Required("CAMERA_UI_EVIDENCE_DIRECTORY");
        string cacheParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.PreviewQa"));
        string cache = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        var photos = new List<object>();
        var savedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var media = new PreviewMediaFixture(repo, evidence);
            using var camera = new PhotoViscaFixture(repo, evidence);
            var connection = new ConnectionSettings("127.0.0.1", camera.Port, media.Address, null);
            string data;
            string[] expectedIds = [PresetIds.Leser, PresetIds.Tisch, PresetIds.Redner];
            using (var session = new NativeQaSession("first-real-photos", "Light", connection: connection, bundleCache: cache))
            {
                try
                {
                data = session.DataDirectory;
                session.Resize(600, 430);
                NavigateSettings(session);
                for (int index = 0; index < expectedIds.Length; index++)
                {
                    session.Find("PresetSelector").AsComboBox().Select(index);
                    WaitEnabled(session, "CapturePresetPhoto");
                    Invoke(session.Find("CapturePresetPhoto"));
                    NativeQaSession.WaitUntil(() => HasName(session, "DraftTarget", "4660"), "Das echte Foto muss die Vorher-/Nachherwerte liefern.", 20);
                    WaitEnabled(session, "SavePreset");
                    Invoke(session.Find("SavePreset"));
                    AppSettings? settings = null;
                    string id = expectedIds[index];
                    NativeQaSession.WaitUntil(() => (settings = ReadSettings(data)).Presets.Single(value => value.Id == id).ImageFileName is not null,
                        "Das echte Testfoto muss unter der ausgewählten festen Positions-ID gespeichert werden.");
                    var preset = settings!.Presets.Single(value => value.Id == id);
                    Assert.That(preset.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
                    string image = Path.Combine(data, "images", preset.ImageFileName!);
                    CheckImage(image, true);
                    string hash = Hash(image);
                    savedHashes.Add(id, hash);
                    File.WriteAllBytes(Path.Combine(evidence, $"local-numbered-test-photo-{id}.png"), File.ReadAllBytes(image));
                    photos.Add(new { Id = id, preset.Target, ImageFileName = preset.ImageFileName, ImageSha256 = hash, Source = "Own loopback numbered RGB test stream; no camera" });
                    OwnPreview(session).Close();
                    WaitQa(data, value => value.Hwnd == 0 && value.ActivePlayers == 0);
                    media.WaitReaders(0);
                }
                session.Find("NavigatePositions").Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(700);
                session.Capture("local-test-photos-positions");
                CheckImage(Path.Combine(session.EvidenceDirectory, "96dpi-Light-local-test-photos-positions.png"), false);
                }
                catch (Exception error)
                {
                    CaptureFailure(session, evidence, error);
                    throw;
                }
            }
            using (var restarted = new NativeQaSession("first-real-photos", "Light", bundleCache: cache))
            {
                restarted.Resize(600, 430);
                var settings = ReadSettings(restarted.DataDirectory);
                foreach (string id in expectedIds)
                {
                    var preset = settings.Presets.Single(value => value.Id == id);
                    Assert.That(preset.ImageFileName, Is.Not.Null);
                    Assert.That(preset.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
                    string image = Path.Combine(restarted.DataDirectory, "images", preset.ImageFileName!);
                    CheckImage(image, true);
                    Assert.That(Hash(image), Is.EqualTo(savedHashes[id]), "Der Neustart muss das unveränderte Foto derselben Position laden.");
                }
                Thread.Sleep(700);
                restarted.Capture("restart-local-test-photos-positions");
                CheckImage(Path.Combine(restarted.EvidenceDirectory, "96dpi-Light-restart-local-test-photos-positions.png"), false);
            }
            camera.AssertHealthy();
            media.WaitReaders(0);
            File.WriteAllText(Path.Combine(evidence, "first-real-test-photos.json"), JsonSerializer.Serialize(new { Source = "Actual snapshots from own numbered loopback test stream", RestartVerified = true, Photos = photos }, Json));
        }
        finally { RemoveOwnCache(cacheParent, cache, evidence, "first-photo-cache-cleanup.json"); }
    }

    [Test]
    public void ModifiedLibraryFromOwnCacheShowsMarkerAndCapturesRealPng()
    {
        string repo = Required("CAMERA_UI_REPO_ROOT");
        string evidence = Required("CAMERA_UI_EVIDENCE_DIRECTORY");
        string replacement = Required("CAMERA_UI_REPLACEMENT_DIRECTORY");
        using var candidate = JsonDocument.Parse(File.ReadAllText(Path.Combine(replacement, "candidate.json")));
        string cacheParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.LibrarySwapQa"));
        string cache = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            using var media = new PreviewMediaFixture(repo, evidence);
            using var camera = new PhotoViscaFixture(repo, evidence);
            var connection = new ConnectionSettings("127.0.0.1", camera.Port, media.Address, null);
            using (var warm = new NativeQaSession("library-warm", "Light", connection: connection, bundleCache: cache))
                Assert.That(warm.Application.HasExited, Is.False);
            string originalDll = Directory.GetFiles(cache, "LibVLCSharp.dll", SearchOption.AllDirectories).Single();
            string bundleRoot = Path.GetDirectoryName(originalDll)!;
            var replacements = new List<object>();
            foreach (var file in candidate.RootElement.GetProperty("Files").EnumerateArray())
            {
                string name = file.GetProperty("Name").GetString()!;
                Assert.That(name, Is.AnyOf("LibVLCSharp.dll", "CameraControl.WinUI.pri"));
                string target = Path.Combine(bundleRoot, name);
                string source = Path.Combine(replacement, name);
                Assert.That(Hash(target), Is.EqualTo(file.GetProperty("OriginalHash").GetString()).IgnoreCase);
                Assert.That(Hash(source), Is.EqualTo(file.GetProperty("ReplacementHash").GetString()).IgnoreCase);
                File.Copy(source, target, overwrite: true);
                Assert.That(Hash(target), Is.EqualTo(Hash(source)));
                replacements.Add(new { Name = name, OriginalHash = file.GetProperty("OriginalHash").GetString(), ReplacementHash = Hash(target), Target = target });
            }
            string snapshotHash;
            object loadedLibrary;
            using (var session = new NativeQaSession("library-replaced", "Light", connection: connection, bundleCache: cache))
            {
                session.Resize(600, 430);
                NavigateSettings(session);
                Invoke(session.Find("OpenPreview"));
                WaitQa(session.DataDirectory, state => state.Phase == "playing" && state.ActivePlayers == 1 && state.DecodedFrames > 0 && state.DisplayedFrames > 0);
                media.WaitReaders(1);
                var preview = OwnPreview(session);
                NativeQaSession.WaitUntil(() => preview.FindFirstDescendant(value => value.ByName("DLL-AUSTAUSCH QA")) is { IsOffscreen: false },
                    "Die tatsächlich ausgeführte geänderte Bibliothek muss ihre QA-Markierung rendern.");
                using var process = Process.GetProcessById(session.Application.ProcessId);
                var module = process.Modules.Cast<ProcessModule>().Single(value => value.ModuleName.Equals("LibVLCSharp.dll", StringComparison.OrdinalIgnoreCase));
                Assert.That(Path.GetFullPath(module.FileName), Is.EqualTo(Path.GetFullPath(originalDll)).IgnoreCase);
                string loadedHash = Hash(module.FileName);
                Assert.That(loadedHash, Is.EqualTo(Hash(Path.Combine(replacement, "LibVLCSharp.dll"))));
                loadedLibrary = new { ProcessId = session.Application.ProcessId, module.FileName, Sha256 = loadedHash, Marker = "DLL-AUSTAUSCH QA" };
                CapturePreview(session, preview, Path.Combine(evidence, "library-replacement-visible.png"));
                WaitEnabled(session, "CapturePresetPhoto");
                Invoke(session.Find("CapturePresetPhoto"));
                NativeQaSession.WaitUntil(() => HasName(session, "DraftTarget", "4660"), "Die Austauschbibliothek muss ein echtes Foto liefern.", 20);
                WaitEnabled(session, "SavePreset");
                Invoke(session.Find("SavePreset"));
                AppSettings? settings = null;
                NativeQaSession.WaitUntil(() => (settings = ReadSettings(session.DataDirectory)).Presets[0].ImageFileName is not null, "Das Austauschfoto muss gespeichert werden.");
                string image = Path.Combine(session.DataDirectory, "images", settings!.Presets[0].ImageFileName!);
                CheckImage(image, true);
                snapshotHash = Hash(image);
                File.Copy(image, Path.Combine(evidence, "library-replacement-photo.png"));
                preview.Close();
                WaitQa(session.DataDirectory, state => state.Hwnd == 0 && state.ActivePlayers == 0);
                media.WaitReaders(0);
            }
            camera.AssertHealthy();
            File.WriteAllText(Path.Combine(evidence, "library-replacement-runtime.json"), JsonSerializer.Serialize(new
            {
                LoadedLibrary = loadedLibrary, Replacements = replacements, RenderedMarkerVerified = true, ActualDecodedSnapshotVerified = true,
                SnapshotSha256 = snapshotHash, PlayersAfterClose = 0, ReadersAfterClose = media.Count("read")
            }, Json));
        }
        finally { RemoveOwnCache(cacheParent, cache, evidence, "library-swap-cache-cleanup.json"); }
    }

    [Test]
    public void TenNativePhotoCyclesPreservePairsAndMainCloseReleasesPlayer()
    {
        var repo = Required("CAMERA_UI_REPO_ROOT");
        var evidence = Required("CAMERA_UI_EVIDENCE_DIRECTORY");
        var cacheParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CameraControl.PreviewQa"));
        var cache = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        var cycleRecords = new List<object>();
        try
        {
            using var media = new PreviewMediaFixture(repo, evidence);
            using var camera = new PhotoViscaFixture(repo, evidence);
            var connection = new ConnectionSettings("127.0.0.1", camera.Port, media.Address, null);
            string? savedImage = null;
            string? savedHash = null;
            string data;
            using (var session = new NativeQaSession("photos-preview", theme: "Light", connection: connection, bundleCache: cache))
            {
                data = session.DataDirectory;
                NavigateSettings(session);
                session.Find("PresetSelector").AsComboBox().Select(0);
                WaitEnabled(session, "CapturePresetPhoto");
                for (int cycle = 1; cycle <= 10; cycle++)
                {
                    session.Window.Focus();
                    Invoke(session.Find("OpenPreview"));
                    var state = WaitQa(data, value => value.Phase == "playing" && value.DecodedFrames > 0 && value.DisplayedFrames > 0);
                    Assert.That(state.ActivePlayers, Is.EqualTo(1));
                    media.WaitReaders(1);
                    var preview = OwnPreview(session);
                    long hwnd = state.Hwnd;
                    session.Window.Focus();
                    Invoke(session.Find("OpenPreview"));
                    Assert.That(WaitQa(data, value => value.Phase == "playing").Hwnd, Is.EqualTo(hwnd));
                    Assert.That(OwnNativePreviewCount(session), Is.EqualTo(1));
                    media.WaitReaders(1);
                    if (cycle == 1)
                    {
                        File.WriteAllText(Path.Combine(evidence, "preview-before-capture.json"), JsonSerializer.Serialize(new
                        {
                            State = state, PreviewHwnd = preview.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(),
                            preview.IsOffscreen, Elements = preview.FindAllDescendants().Select(value => new
                            {
                                Id = value.Properties.AutomationId.ValueOrDefault, Name = value.Properties.Name.ValueOrDefault,
                                Type = value.Properties.ControlType.ValueOrDefault.ToString(), IsOffscreen = value.Properties.IsOffscreen.ValueOrDefault,
                                Bounds = NativeQaSession.Rect(value)
                            }).ToArray()
                        }, Json));
                        RecordNativeBundle(session, repo, cache, evidence);
                        CapturePreview(session, preview, Path.Combine(evidence, "preview-1.png"));
                        Thread.Sleep(1200);
                        CapturePreview(session, preview, Path.Combine(evidence, "preview-2.png"));
                        Assert.That(Hash(Path.Combine(evidence, "preview-1-video.png")), Is.Not.EqualTo(Hash(Path.Combine(evidence, "preview-2-video.png"))));
                    }
                    session.Window.Focus();
                    WaitEnabled(session, "CapturePresetPhoto");
                    Invoke(session.Find("CapturePresetPhoto"));
                    NativeQaSession.WaitUntil(() => HasName(session, "DraftTarget", "4660"), "Foto muss Werte derselben Position liefern.", 20);
                    WaitEnabled(session, "SavePreset");
                    Invoke(session.Find("SavePreset"));
                    AppSettings? settings = null;
                    NativeQaSession.WaitUntil(() =>
                    {
                        settings = ReadSettings(data);
                        return settings.Presets[0].ImageFileName is { } name && name != savedImage;
                    }, "Das Foto muss unter derselben Positions-ID gespeichert werden.");
                    var preset = settings!.Presets.Single(value => value.Id == PresetIds.Leser);
                    Assert.That(preset.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
                    Assert.That(settings.Presets.Where(value => value.Id != PresetIds.Leser).All(value => value.ImageFileName is null), Is.True);
                    savedImage = preset.ImageFileName;
                    var imagePath = Path.Combine(data, "Images", savedImage!);
                    if (!File.Exists(imagePath)) imagePath = Path.Combine(data, "images", savedImage!);
                    CheckImage(imagePath, fullSnapshot: true);
                    savedHash = Hash(imagePath);
                    File.WriteAllBytes(Path.Combine(evidence, $"photo-{cycle}.png"), File.ReadAllBytes(imagePath));
                    OwnPreview(session).Close();
                    var closed = WaitQa(data, value => value.Hwnd == 0 && value.ActivePlayers == 0);
                    media.WaitReaders(0);
                    cycleRecords.Add(new { Cycle = cycle, PreviewHwnd = hwnd, SavedId = preset.Id, preset.Target,
                        ImageSha256 = savedHash, PlayersAfterClose = closed.ActivePlayers, ReadersAfterClose = media.Count("read") });
                }
                session.Capture("saved-photo");
                camera.ChangeNextPhotoPosition();
                Invoke(session.Find("CapturePresetPhoto"));
                NativeQaSession.WaitUntil(() => HasName(session, "OperationMessage", "geändert"),
                    "Änderung zwischen Vorher-/Nachherwerten muss die Aufnahme ablehnen.", 20);
                AssertSavedUnchanged(data, savedImage!, savedHash!);
                Invoke(session.Find("CancelPreset"));
                camera.StablePosition();
                media.StopSender();
                WaitQa(data, value => value.Phase == "error", seconds: 20);
                Invoke(session.Find("CapturePresetPhoto"));
                NativeQaSession.WaitUntil(() => HasName(session, "OperationMessage", "unverändert"),
                    "Streamfehler muss als Foto-Fehler angezeigt werden.", 20);
                AssertSavedUnchanged(data, savedImage!, savedHash!);
                OwnPreview(session).Close();
                WaitQa(data, value => value.ActivePlayers == 0 && value.Hwnd == 0);
                media.WaitReaders(0);
                media.StartSender();
                Invoke(session.Find("OpenPreview"));
                WaitQa(data, value => value.Phase == "playing" && value.ActivePlayers == 1);
                media.WaitReaders(1);
                session.Window.Focus();
                using var appProcess = Process.GetProcessById(session.Application.ProcessId);
                _ = appProcess.Handle;
                session.Window.Close();
                NativeQaSession.WaitUntil(() => session.Application.HasExited, "Hauptfensterschluss muss die Vorschau geordnet beenden.", 15);
                Assert.That(appProcess.ExitCode, Is.Zero);
                var mainClosed = ReadQa(data);
                Assert.That(mainClosed.ActivePlayers, Is.Zero);
                Assert.That(mainClosed.Hwnd, Is.Zero);
                Assert.That(mainClosed.Disposed, Is.True);
                media.WaitReaders(0);
            }
            using (var restarted = new NativeQaSession("photos-preview", theme: "Light", bundleCache: cache))
            {
                AssertSavedUnchanged(restarted.DataDirectory, savedImage!, savedHash!);
                restarted.Capture("restart-saved-photo");
                NavigateSettings(restarted);
                restarted.Find("PresetSelector").AsComboBox().Select(0);
                NativeQaSession.WaitUntil(() => HasName(restarted, "DraftTarget", "4660"), "Neustart muss die richtigen Werte zeigen.");
                restarted.Capture("restart-draft-photo");
            }
            camera.AssertHealthy();
            media.WaitReaders(0);
            File.WriteAllText(Path.Combine(evidence, "photo-lifecycle.json"), JsonSerializer.Serialize(cycleRecords, Json));
        }
        finally
        {
            string full = Path.GetFullPath(cache);
            if (!full.StartsWith(cacheParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(full), "N", out _)) throw new InvalidOperationException("Der Cache ist nicht der eigene QA-Laufordner.");
            int files = Directory.Exists(full) ? Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length : 0;
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            File.WriteAllText(Path.Combine(evidence, "preview-cache-cleanup.json"), JsonSerializer.Serialize(new { Path = full, FilesBeforeCleanup = files,
                ExistsAfterCleanup = Directory.Exists(full) }, Json));
        }
    }

    private static void NavigateSettings(NativeQaSession session)
    {
        session.Window.Focus();
        var navigation = session.Find("NavigateSettings");
        Assert.That(navigation.IsOffscreen, Is.False);
        if (navigation.Patterns.SelectionItem.IsSupported) navigation.Patterns.SelectionItem.Pattern.Select();
        else if (navigation.Patterns.Invoke.IsSupported) navigation.Patterns.Invoke.Pattern.Invoke();
        else Assert.Fail("Die native Navigation besitzt keine Selection-/Invoke-Operation.");
        NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(value => value.ByAutomationId("PresetSelector")) is not null,
            "Die native Einstellungsseite muss erscheinen.");
    }

    private static void CaptureFailure(NativeQaSession session, string evidence, Exception failure)
    {
        File.WriteAllText(Path.Combine(evidence, "photo-primary-error.txt"), failure.ToString());
        var diagnosticErrors = new List<string>();
        Thread.Sleep(TimeSpan.FromSeconds(12));
        try
        {
            string state = Path.Combine(session.DataDirectory, "preview-qa.json");
            if (File.Exists(state)) File.WriteAllText(Path.Combine(evidence, "preview-before-cleanup.json"), JsonSerializer.Serialize(ReadQa(session.DataDirectory), Json));
            var windows = session.Application.GetAllTopLevelWindows(session.Automation);
            File.WriteAllText(Path.Combine(evidence, "photo-failure-uia.json"), JsonSerializer.Serialize(windows.Select(window => new
            {
                window.Title, Hwnd = window.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(), window.IsOffscreen,
                Elements = window.FindAllDescendants().Select(value => new { Id = value.Properties.AutomationId.ValueOrDefault,
                    value.Name, value.IsOffscreen, value.IsEnabled, Type = value.ControlType.ToString(), Bounds = NativeQaSession.Rect(value) }).ToArray()
            }).ToArray(), Json));
            foreach (var window in windows.Where(value => value.Title == "Vorschau — Versammlungs-Kamerasteuerung"))
            {
                var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
                ShowWindow(handle, 9);
                SetForegroundWindow(handle);
                DwmFlush();
                Thread.Sleep(200);
                DwmFlush();
                Assert.That(GetWindowRect(handle, out var bounds), Is.True);
                using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                using var graphics = Graphics.FromImage(bitmap);
                var dc = graphics.GetHdc();
                try { Assert.That(PrintWindow(handle, dc, 2), Is.True); }
                finally { graphics.ReleaseHdc(dc); }
                bitmap.Save(Path.Combine(evidence, "photo-failure-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (Exception error) { diagnosticErrors.Add(error.ToString()); }
        try { session.Capture("photo-failure"); }
        catch (Exception error) { diagnosticErrors.Add(error.ToString()); }
        File.WriteAllText(Path.Combine(evidence, "photo-diagnostic-errors.json"), JsonSerializer.Serialize(diagnosticErrors, Json));
    }

    private static void WaitEnabled(NativeQaSession session, string id) => NativeQaSession.WaitUntil(() => session.Find(id).IsEnabled,
        "Das native Element muss bedienbar sein: " + id, 20);

    private static void Invoke(AutomationElement element)
    {
        if (element.Patterns.ScrollItem.IsSupported) element.Patterns.ScrollItem.Pattern.ScrollIntoView();
        element.AsButton().Invoke();
    }

    private static bool HasName(NativeQaSession session, string id, string expected) =>
        session.Window.FindFirstDescendant(value => value.ByAutomationId(id))?.Name.Contains(expected, StringComparison.Ordinal) == true;

    private static Window OwnPreview(NativeQaSession session)
    {
        nint hwnd = new(ReadQa(session.DataDirectory).Hwnd);
        Assert.That(hwnd, Is.Not.EqualTo(nint.Zero), "Die eigene Vorschau muss ein echtes HWND melden.");
        Assert.That(IsWindow(hwnd), Is.True);
        GetWindowThreadProcessId(hwnd, out uint processId);
        Assert.That(processId, Is.EqualTo((uint)session.Application.ProcessId), "Das Vorschau-HWND muss zur eigenen QA-App gehören.");
        Assert.That(NativeTitle(hwnd), Is.EqualTo("Vorschau — Versammlungs-Kamerasteuerung"));
        var window = session.Automation.FromHandle(hwnd).AsWindow();
        Assert.That(window.Properties.NativeWindowHandle.ValueOrDefault, Is.EqualTo(hwnd));
        return window;
    }

    private static int OwnNativePreviewCount(NativeQaSession session)
    {
        int count = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == session.Application.ProcessId && NativeTitle(hwnd) == "Vorschau — Versammlungs-Kamerasteuerung") count++;
            return true;
        }, 0);
        return count;
    }

    private static string NativeTitle(nint hwnd)
    {
        var text = new StringBuilder(256);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static AppSettings ReadSettings(string data) => ReadJson<AppSettings>(Path.Combine(data, "settings.json"));
    private static PreviewState ReadQa(string data) => ReadJson<PreviewState>(Path.Combine(data, "preview-qa.json"));
    private static T ReadJson<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Json) ?? throw new InvalidDataException("Der QA-Datensatz ist leer.");
    }

    private static PreviewState WaitQa(string data, Func<PreviewState, bool> predicate, int seconds = 15)
    {
        PreviewState? state = null;
        NativeQaSession.WaitUntil(() =>
        {
            try { state = ReadQa(data); return predicate(state); }
            catch (IOException) { return false; }
        }, "Die Vorschau muss den erwarteten echten Zustand erreichen.", seconds);
        return state!;
    }

    private static void AssertSavedUnchanged(string data, string image, string expectedHash)
    {
        var saved = ReadSettings(data).Presets[0];
        Assert.That(saved.ImageFileName, Is.EqualTo(image));
        Assert.That(saved.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        var path = Path.Combine(data, "images", image);
        Assert.That(Hash(path), Is.EqualTo(expectedHash));
    }

    private static void RecordNativeBundle(NativeQaSession session, string repo, string cache, string evidence)
    {
        using var process = Process.GetProcessById(session.Application.ProcessId);
        var modules = process.Modules.Cast<ProcessModule>().Where(value => value.ModuleName is "libvlc.dll" or "libvlccore.dll")
            .Select(value => new { value.ModuleName, value.FileName, Sha256 = Hash(value.FileName) }).ToArray();
        Assert.That(modules.Length, Is.EqualTo(2));
        Assert.That(modules.All(value => value.FileName.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), Is.True);
        File.WriteAllText(Path.Combine(evidence, "integrated-native-modules.json"), JsonSerializer.Serialize(modules, Json));
        string root = Path.GetDirectoryName(modules.Single(value => value.ModuleName == "libvlc.dll").FileName)!;
        string? manifestPath = Environment.GetEnvironmentVariable("CAMERA_UI_VLC_MANIFEST");
        var files = new List<object>();
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(manifestPath)));
            Assert.That(manifest.RootElement.GetProperty("Package").GetString(), Is.EqualTo("VideoLAN.LibVLC.Windows"));
            Assert.That(manifest.RootElement.GetProperty("Version").GetString(), Is.EqualTo("3.0.24"));
            Assert.That(manifest.RootElement.GetProperty("Architecture").GetString(), Is.EqualTo("win-x64"));
            foreach (var expected in manifest.RootElement.GetProperty("Files").EnumerateArray())
            {
                string relative = expected.GetProperty("Path").GetString()!;
                string extracted = Path.GetFullPath(Path.Combine(root, relative));
                Assert.That(extracted.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(File.Exists(extracted), Is.True, relative);
                string hash = Hash(extracted);
                Assert.That(hash, Is.EqualTo(expected.GetProperty("Sha256").GetString()).IgnoreCase, relative);
                files.Add(new { Path = relative, Sha256 = hash });
            }
        }
        else
        {
        string package = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget/packages/videolan.libvlc.windows/3.0.24/build/x64");
        foreach (var source in Directory.GetFiles(package, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(package, source);
            if (!(relative.StartsWith("libvlc.", StringComparison.Ordinal) || relative.StartsWith("libvlccore.", StringComparison.Ordinal) ||
                relative.StartsWith("plugins\\", StringComparison.Ordinal) || relative.StartsWith("lua\\", StringComparison.Ordinal) || relative.StartsWith("hrtfs\\", StringComparison.Ordinal))) continue;
            var extracted = Path.Combine(root, relative);
            Assert.That(File.Exists(extracted), Is.True, relative);
            Assert.That(Hash(extracted), Is.EqualTo(Hash(source)), relative);
            files.Add(new { Path = relative, Sha256 = Hash(extracted) });
        }
        }
        Assert.That(files.Count, Is.EqualTo(422));
        File.WriteAllText(Path.Combine(evidence, "integrated-native-bundle-files.json"), JsonSerializer.Serialize(files, Json));
    }

    private static void CapturePreview(NativeQaSession session, Window preview, string path)
    {
        var previous = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var hwnd = preview.Properties.NativeWindowHandle.ValueOrDefault;
            preview.Focus();
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
            Thread.Sleep(200);
            Assert.That(GetWindowRect(hwnd, out var bounds), Is.True);
            using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            using var graphics = Graphics.FromImage(bitmap);
            var dc = graphics.GetHdc();
            try { Assert.That(PrintWindow(hwnd, dc, 2), Is.True); } finally { graphics.ReleaseHdc(dc); }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            CheckImage(path, false);
            var button = preview.FindFirstDescendant(value => value.ByAutomationId("PreviewReconnect"))!;
            var status = preview.FindFirstDescendant(value => value.ByAutomationId("PreviewStatus"))!;
            double scale = GetDpiForWindow(hwnd) / 96.0;
            int top = (int)Math.Ceiling(button.BoundingRectangle.Bottom - bounds.Top + 8 * scale);
            int bottom = (int)Math.Floor(status.BoundingRectangle.Top - bounds.Top - 8 * scale);
            int margin = (int)Math.Ceiling(16 * scale);
            using var crop = bitmap.Clone(new Rectangle(margin, top, bitmap.Width - 2 * margin, bottom - top), bitmap.PixelFormat);
            string cropPath = Path.ChangeExtension(path, null) + "-video.png";
            crop.Save(cropPath, System.Drawing.Imaging.ImageFormat.Png);
            CheckImage(cropPath, false);
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    private static void CheckImage(string path, bool fullSnapshot)
    {
        using var bitmap = new Bitmap(path);
        if (fullSnapshot) { Assert.That(bitmap.Width, Is.EqualTo(640)); Assert.That(bitmap.Height, Is.EqualTo(360)); }
        int[] rgb = [0, 0, 0];
        for (int y = 0; y < bitmap.Height; y += 4)
        for (int x = 0; x < bitmap.Width; x += 4)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.R > 190 && pixel.G < 70 && pixel.B < 70) rgb[0]++;
            if (pixel.G > 190 && pixel.R < 70 && pixel.B < 70) rgb[1]++;
            if (pixel.B > 190 && pixel.R < 70 && pixel.G < 70) rgb[2]++;
        }
        Assert.That(rgb.All(value => value >= 20), Is.True, "Echte RGB-Testmarker müssen sichtbar sein.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string Required(string name) => Path.GetFullPath(Environment.GetEnvironmentVariable(name) ?? throw new AssertionException(name + " fehlt."));
    private static void RemoveOwnCache(string parent, string cache, string evidence, string file)
    {
        string full = Path.GetFullPath(cache);
        if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new InvalidOperationException("Der Cache ist nicht der eigene QA-Laufordner.");
        int count = Directory.Exists(full) ? Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length : 0;
        if (Directory.Exists(full)) Directory.Delete(full, true);
        File.WriteAllText(Path.Combine(evidence, file), JsonSerializer.Serialize(new { Path = full, FilesBeforeCleanup = count, ExistsAfterCleanup = Directory.Exists(full) }, Json));
    }
    private sealed record PreviewState(long Hwnd, int ActivePlayers, string Phase, int DecodedFrames, int DisplayedFrames, bool Disposed);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rectangle);
    private delegate bool WindowCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
