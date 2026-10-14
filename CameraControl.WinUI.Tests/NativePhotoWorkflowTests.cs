using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using CameraControl.AppCore;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("NativePhotoWorkflow"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativePhotoWorkflowTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    [Test]
    public void ControllerPhotoDialogSavesNamedViewAndCancelPreservesIt()
    {
        Assert.That(Environment.UserName, Is.EqualTo("WDAGUtilityAccount").IgnoreCase);
        string evidence = Required("CAMERA_UI_EVIDENCE_DIRECTORY"), repo = Required("CAMERA_UI_REPO_ROOT");
        string cacheParent = Path.Combine(Path.GetTempPath(), "CameraControl.ConfirmedPhoto.Qa");
        string cache = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
        var screenshots = new List<string>();
        var pids = new List<int>();
        AppSettings? stored = null;
        string? tableHash = null, homeHash = null;
        try
        {
            using var media = new PreviewMediaFixture(repo, evidence);
            using var camera = new PhotoViscaFixture(repo, evidence, allowManualControl: true);
            using (var session = new NativeQaSession("confirmed-photo", "Light",
                connection: new ConnectionSettings("127.0.0.1", camera.Port, media.Address, null), bundleCache: cache))
            {
                pids.Add(session.Application.ProcessId);
                try
                {
                    session.Resize(600, 430);
                    Navigate(session, "NavigateSettings", "ConnectionHost");
                    Capture(session, "01-connection", screenshots);
                    Invoke(session, "ConnectionSave");
                    NativeQaSession.WaitUntil(() => session.Window.FindFirstDescendant(c => c.ByAutomationId("ControlStatus"))?.Properties.Name.ValueOrDefault?.Contains("Verbunden", StringComparison.Ordinal) == true,
                        "Speichern und verbinden muss die lokale Steuerung verbinden.");
                    Tab(session, "SettingsPositionsSection");
                    WaitVisible(session, "PresetSelector");
                    session.Find("PresetSelector").AsComboBox().Select(1);
                    NativeQaSession.WaitUntil(() => session.Find("PresetSelector").AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault == "Tisch", "Die gemeinsame Ansicht muss Tisch sein.");
                    session.Find("PresetName").AsTextBox().Text = "Tisch vorne";
                    Assert.That(ReadSettings(session.DataDirectory).Presets[1].Name, Is.EqualTo("Tisch"), "Der Name wird erst auf ausdrückliche Aktion gespeichert.");
                    Invoke(session, "SavePreset");
                    NativeQaSession.WaitUntil(() => ReadSettings(session.DataDirectory).Presets[1].Name == "Tisch vorne", "Name speichern muss nur die gewählte Ansicht benennen.");
                    Capture(session, "02-view-settings", screenshots);
                    Invoke(session, "OpenControllerForPhoto");
                    WaitVisible(session, "CaptureSelectedPhoto");
                    Assert.That(session.Window.FindFirstDescendant(c => c.ByAutomationId("SaveSelectedPosition")), Is.Null, "Die alte getrennte Positionssave-Aktion entfällt.");
                    Capture(session, "03-controller", screenshots);
                    Write(evidence, "early-screens-ready.json", new { Passed = true, Count = 3, Screenshots = screenshots.ToArray() });

                    AppSettings before = ReadSettings(session.DataDirectory);
                    string original = JsonSerializer.Serialize(before.Presets, Json);
                    Invoke(session, "CaptureSelectedPhoto");
                    WaitDialog(session, "Tisch vorne");
                    Assert.That(JsonSerializer.Serialize(ReadSettings(session.DataDirectory).Presets, Json), Is.EqualTo(original));
                    Capture(session, "04-photo-dialog", screenshots);
                    Invoke(session, "PhotoSaveCancel");
                    WaitPhotoReady(session);
                    Assert.That(JsonSerializer.Serialize(ReadSettings(session.DataDirectory).Presets, Json), Is.EqualTo(original));

                    Invoke(session, "CaptureSelectedPhoto");
                    WaitDialog(session, "Tisch vorne");
                    Invoke(session, "PhotoSaveConfirm");
                    NativeQaSession.WaitUntil(() => (stored = ReadSettings(session.DataDirectory)).Presets[1].ImageFileName is not null, "Bestätigung muss das Foto ohne zweiten Speicherschritt ablegen.");
                    WaitPhotoReady(session);
                    var table = stored!.Presets[1];
                    Assert.That(table.Name, Is.EqualTo("Tisch vorne"));
                    Assert.That(table.Target, Is.EqualTo(new CameraPosition(-1, 1, 4660)));
                    Assert.That(stored.Presets.Where(value => value.Id != PresetIds.Tisch), Is.EqualTo(before.Presets.Where(value => value.Id != PresetIds.Tisch)));
                    tableHash = CheckPhoto(session.DataDirectory, table.ImageFileName!);
                    File.Copy(Path.Combine(session.DataDirectory, "Images", table.ImageFileName!), Path.Combine(evidence, "saved-tisch.png"));
                    session.Resize(960, 560);
                    WaitVisible(session, "PresetTisch");
                    Capture(session, "05-saved-combined", screenshots);

                    session.Find("ControlPresetSelector").AsComboBox().Select(3);
                    NativeQaSession.WaitUntil(() => session.Find("ControlPresetSelector").AsComboBox().SelectedItem?.Properties.Name.ValueOrDefault == "Home", "Home muss ausgewählt werden.");
                    int queries = camera.Count("pan-query") + camera.Count("zoom-query");
                    Invoke(session, "CaptureSelectedPhoto");
                    WaitDialog(session, "Home");
                    session.Resize(360, 280);
                    WaitVisible(session, "PhotoSaveConfirm"); WaitVisible(session, "PhotoSaveCancel");
                    Capture(session, "06-minimum-dialog", screenshots);
                    Invoke(session, "PhotoSaveConfirm");
                    NativeQaSession.WaitUntil(() => (stored = ReadSettings(session.DataDirectory)).Presets[3].ImageFileName is not null, "Home speichert nur sein Foto.");
                    WaitPhotoReady(session);
                    Assert.That(stored!.Presets[3].Target, Is.Null);
                    Assert.That(camera.Count("pan-query") + camera.Count("zoom-query"), Is.EqualTo(queries));
                    Assert.That(CheckPhoto(session.DataDirectory, stored.Presets[1].ImageFileName!), Is.EqualTo(tableHash));
                    homeHash = CheckPhoto(session.DataDirectory, stored.Presets[3].ImageFileName!);
                    camera.AssertHealthy();
                }
                catch { try { session.Capture("failure"); } catch { } throw; }
            }
            media.WaitReaders(0);
            using (var restarted = new NativeQaSession("confirmed-photo", "Light", bundleCache: cache))
            {
                pids.Add(restarted.Application.ProcessId);
                restarted.Resize(600, 430);
                NativeQaSession.WaitUntil(() => restarted.Window.FindFirstDescendant(c => c.ByAutomationId("PresetTisch"))?.Properties.Name.ValueOrDefault?.Contains("Tisch vorne", StringComparison.Ordinal) == true,
                    "Die tatsächliche App muss nach Neustart den gespeicherten Namen laden.");
                var settings = ReadSettings(restarted.DataDirectory);
                Assert.That(settings.Presets, Is.EqualTo(stored!.Presets));
                Assert.That(CheckPhoto(restarted.DataDirectory, settings.Presets[1].ImageFileName!), Is.EqualTo(tableHash));
                Assert.That(CheckPhoto(restarted.DataDirectory, settings.Presets[3].ImageFileName!), Is.EqualTo(homeHash));
                Capture(restarted, "07-restart-images", screenshots);
                camera.AssertHealthy();
            }
            media.WaitReaders(0);
            Write(evidence, "adjustment-result.json", new { Passed = true, DialogName = "Tisch vorne", CancelPreserved = true,
                SingleSave = true, TableTarget = new CameraPosition(-1, 1, 4660), HomeHasNoTarget = true,
                RestartVerified = true, TableImageHash = tableHash, HomeImageHash = homeHash, Screenshots = screenshots,
                Source = "Actual native UI and own loopback numbered RTSP stream; no physical camera" });
        }
        finally
        {
            var remaining = pids.Where(pid => { try { return !Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { return false; } }).ToArray();
            Assert.That(remaining, Is.Empty);
            string full = Path.GetFullPath(cache);
            Assert.That(full.StartsWith(Path.GetFullPath(cacheParent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(full)) { Assert.That(File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint), Is.False); Directory.Delete(full, true); }
            Write(evidence, "adjustment-cleanup.json", new { ProcessIds = pids, RemainingOwnProcessIds = remaining, CacheRemoved = !Directory.Exists(full), HostInputs = false });
        }
    }

    private static AutomationElement WaitVisible(NativeQaSession session, string id)
    {
        AutomationElement? found = null;
        NativeQaSession.WaitUntil(() => (found = session.Window.FindFirstDescendant(c => c.ByAutomationId(id))) is { } element && Visible(element), "Das echte UI-Element muss sichtbar sein: " + id, 20);
        return found!;
    }
    private static bool Visible(AutomationElement element) => !element.Properties.IsOffscreen.ValueOrDefault && element.Properties.BoundingRectangle.ValueOrDefault.Width > 0 && element.Properties.BoundingRectangle.ValueOrDefault.Height > 0;
    private static void Invoke(NativeQaSession session, string id)
    {
        var button = WaitVisible(session, id);
        NativeQaSession.WaitUntil(() => button.Properties.IsEnabled.ValueOrDefault, "Die Aktion muss freigegeben sein: " + id, 20);
        Assert.That(button.Patterns.Invoke.IsSupported, Is.True); button.Patterns.Invoke.Pattern.Invoke();
    }
    private static void Tab(NativeQaSession session, string id)
    {
        var tab = WaitVisible(session, id);
        Assert.That(tab.Patterns.SelectionItem.IsSupported, Is.True); tab.Patterns.SelectionItem.Pattern.Select();
        NativeQaSession.WaitUntil(() => tab.Patterns.SelectionItem.Pattern.IsSelected, "Der native Tab muss ausgewählt sein: " + id);
    }
    private static void Navigate(NativeQaSession session, string tab, string anchor) { Tab(session, tab); WaitVisible(session, anchor); }
    private static void WaitDialog(NativeQaSession session, string name)
    {
        NativeQaSession.WaitUntil(() =>
        {
            string path = Path.Combine(session.DataDirectory, "preview-qa.json");
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var state = JsonDocument.Parse(stream);
                return state.RootElement.GetProperty("Phase").GetString() == "playing" &&
                    state.RootElement.GetProperty("DecodedFrames").GetInt32() > 0 && state.RootElement.GetProperty("DisplayedFrames").GetInt32() > 0;
            }
            catch (FileNotFoundException) { return false; }
            catch (IOException error) when ((error.HResult & 65535) is 32 or 33) { return false; }
        }, "Der erste native Decoderstart muss ein tatsächlich decodiertes und dargestelltes Bild liefern.", 60);
        WaitVisible(session, "PhotoSaveDialog"); WaitVisible(session, "PhotoSaveImage");
        NativeQaSession.WaitUntil(() => session.Find("PhotoSaveDialog").Properties.Name.ValueOrDefault == $"Foto für „{name}“ speichern?", "Der Fotodialog muss den tatsächlichen Ansichtsnamen nennen.", 20);
        WaitVisible(session, "PhotoSaveConfirm"); WaitVisible(session, "PhotoSaveCancel");
    }
    private static void WaitPhotoReady(NativeQaSession session) => NativeQaSession.WaitUntil(() =>
        session.Window.FindFirstDescendant(c => c.ByAutomationId("PhotoSaveDialog")) is null &&
        session.Window.FindFirstDescendant(c => c.ByAutomationId("CaptureSelectedPhoto"))?.Properties.IsEnabled.ValueOrDefault == true, "Der Fotoauftrag muss fertig und der Dialog geschlossen sein.", 20);
    private static void Capture(NativeQaSession session, string name, List<string> images)
    {
        var timer = Stopwatch.StartNew(); int attempt = 0;
        while (true)
        {
            try { session.Capture(name); break; }
            catch (AssertionException error) when (error.Message.Contains("Eine schwarze oder leere Aufnahme ist kein UI-Nachweis.", StringComparison.Ordinal) && timer.Elapsed < TimeSpan.FromSeconds(10))
            { File.Copy(Path.Combine(session.EvidenceDirectory, $"{session.Dpi}dpi-Light-{name}.png"), Path.Combine(session.EvidenceDirectory, $"{name}-rejected-{++attempt}.png")); Thread.Sleep(100); }
        }
        images.Add(Path.Combine(session.EvidenceDirectory, $"{session.Dpi}dpi-Light-{name}.png"));
    }
    private static string CheckPhoto(string data, string file)
    {
        string path = Path.Combine(data, "Images", file);
        using var bitmap = new Bitmap(path);
        Assert.That(bitmap.Width, Is.GreaterThan(100)); Assert.That(bitmap.Height, Is.GreaterThan(100));
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
    private static AppSettings ReadSettings(string data)
    {
        using var stream = new FileStream(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<AppSettings>(stream, Json) ?? throw new AssertionException("Die eigene Einstellungsdatei ist leer.");
    }
    private static string Required(string name) => Path.GetFullPath(Environment.GetEnvironmentVariable(name) ?? throw new AssertionException(name + " fehlt."));
    private static void Write(string folder, string name, object value) => File.WriteAllText(Path.Combine(folder, name), JsonSerializer.Serialize(value, Json));
}
