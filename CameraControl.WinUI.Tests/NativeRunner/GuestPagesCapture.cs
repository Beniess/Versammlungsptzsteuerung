using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

internal static class GuestPagesCapture
{
    internal static void Run(string evidence)
    {
        Assert.That(Environment.UserName, Is.EqualTo("WDAGUtilityAccount").IgnoreCase);
        var pages = new List<object>();
        using (var qa = new NativeQaSession("compact-pages", "Light", "empty"))
        {
            Assert.That(qa.Dpi, Is.EqualTo(96));
            qa.Resize(600, 430);
            void Capture(string name)
            {
                Assert.That(GetForegroundWindow(), Is.EqualTo(qa.Window.Properties.NativeWindowHandle.ValueOrDefault),
                    "Die eigene Gast-App muss tatsächlich im Vordergrund sein.");
                qa.Capture(name);
                var path = Path.Combine(qa.EvidenceDirectory, $"96dpi-Light-{name}.png");
                Assert.That(File.Exists(path), Is.True);
                pages.Add(new { Page = name, Screenshot = path, ProcessId = qa.Application.ProcessId, Dpi = qa.Dpi, Width = 600, Height = 430 });
            }
            void Navigate(string id)
            {
                qa.Window.Focus();
                qa.Find(id).Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(300);
            }

            Capture("Positionen");
            Navigate("NavigateControl");
            NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { IsOffscreen: false }, "Das konkrete Pan-/Tilt-Steuerfeld fehlt.");
            Capture("Steuerung");
            Navigate("NavigateSettings");
            NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("ConnectionHost")) is not null, "Die Einstellungsseite fehlt.");
            Capture("Einstellungen");
            var selector = qa.Find("PresetSelector");
            selector.Focus();
            if (selector.Patterns.ScrollItem.IsSupported) selector.Patterns.ScrollItem.Pattern.ScrollIntoView();
            Thread.Sleep(200);
            var photo = qa.Find("CapturePresetPhoto");
            if (photo.IsOffscreen && photo.Patterns.ScrollItem.IsSupported) photo.Patterns.ScrollItem.Pattern.ScrollIntoView();
            NativeQaSession.WaitUntil(() => !qa.Find("CapturePresetPhoto").IsOffscreen, "Die Fotoaktionen müssen sichtbar sein.");
            Capture("Fotoeditor");
            Navigate("NavigatePositions");
            NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PresetHome")) is not null, "Die Positionsseite fehlt.");
            File.WriteAllText(Path.Combine(evidence, "ui-pages.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Pages = pages, AppVersion = FileVersionInfo.GetVersionInfo(Environment.GetEnvironmentVariable("CAMERA_UI_EXE")!).FileVersion,
                ActualForegroundProved = true, PreviewOpened = false, CameraEndpointConfigured = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
}
