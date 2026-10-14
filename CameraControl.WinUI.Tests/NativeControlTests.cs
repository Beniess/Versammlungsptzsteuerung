using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("Controls"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class NativeControlTests
{
    [Test]
    public void PointerReleaseOutsideAndWindowDeactivationSendConfirmedStop()
    {
        using var server = new UiViscaServer();
        using var qa = new NativeQaSession("control-pointer-stop", cameraPort: server.Port);
        NativeQaSession.WaitUntil(() => qa.Find("PresetHome").IsEnabled, "Die lokale QA-Verbindung fehlt.");
        Assert.That(qa.Find("PresetLeser").IsEnabled, Is.False);
        qa.Find("NavigateControl").Click();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { IsEnabled: true }, "Das Steuerfeld ist nicht bereit.");
        var pan = qa.Find("PanTiltPad");
        try
        {
            Mouse.MoveTo(PointAt(pan.BoundingRectangle, .75, .25));
            Mouse.Down(MouseButton.Left);
            server.WaitFor(bytes => bytes.SequenceEqual(new byte[] { 0x81, 0x01, 0x06, 0x01, 12, 12, 2, 1, 0xff }));
            qa.Capture("pan-moving");
            var frame = qa.Window.BoundingRectangle;
            Mouse.MoveTo((int)frame.Right + 20, (int)frame.Bottom + 20);
            Mouse.Up(MouseButton.Left);
            server.WaitFor(IsPanStop);
            NativeQaSession.WaitUntil(() => qa.Find("PanTiltPad").IsEnabled, "Der Pan-Stopp muss vor der nächsten Eingabe bestätigt sein.");
            var scrollElement = qa.Find("ControlScroll");
            var scroll = scrollElement.Patterns.Scroll.IsSupported ? scrollElement.Patterns.Scroll.Pattern : null;
            if (qa.Find("ZoomPad").IsOffscreen)
            {
                Assert.That(scroll, Is.Not.Null, "Ein verdecktes Zoomfeld benötigt die native Scrollfläche.");
                scroll!.SetScrollPercent(-1, 100);
            }
            NativeQaSession.WaitUntil(() => !qa.Find("ZoomPad").IsOffscreen, "Das Zoomfeld muss über die native Scrollfläche erreichbar sein.");
            Mouse.MoveTo(PointAt(qa.Find("ZoomPad").BoundingRectangle, .75, .5)); Mouse.Down(MouseButton.Left);
            server.WaitFor(bytes => bytes.SequenceEqual(new byte[] { 0x81, 0x01, 0x04, 0x07, 0x24, 0xff }));
            qa.Capture("zoom-moving");
            Mouse.MoveTo((int)frame.Right + 20, (int)frame.Bottom + 20); Mouse.Up(MouseButton.Left);
            server.WaitFor(bytes => bytes.SequenceEqual(new byte[] { 0x81, 0x01, 0x04, 0x07, 0x00, 0xff }));
            NativeQaSession.WaitUntil(() => qa.Find("PanTiltPad").IsEnabled, "Die Sitzung muss den Zoom-Stopp bestätigen.");
            if (scroll?.VerticallyScrollable.Value == true) scroll.SetScrollPercent(-1, 0);
            var priorStops = server.Count(IsPanStop);
            pan.Focus();
            Keyboard.Press(VirtualKeyShort.RIGHT);
            server.WaitFor(bytes => bytes.Length == 9 && bytes[4] == 24 && bytes[6] == 2);
            qa.Window.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Minimized);
            server.WaitForCount(IsPanStop, priorStops + 1);
            Keyboard.Release(VirtualKeyShort.RIGHT);
            qa.Window.Patterns.Window.Pattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
            qa.Window.Focus();
            NativeQaSession.WaitUntil(() => qa.Find("PanTiltPad").IsEnabled, "Der Deaktivierungsstopp muss vor neuer Eingabe bestätigt sein.");
            priorStops = server.Count(IsPanStop);
            pan.Focus(); Keyboard.Press(VirtualKeyShort.LEFT);
            server.WaitFor(bytes => bytes.Length == 9 && bytes[4] == 24 && bytes[6] == 1);
            qa.Find("NavigatePositions").Click();
            server.WaitForCount(IsPanStop, priorStops + 1);
            Keyboard.Release(VirtualKeyShort.LEFT);
            NativeQaSession.WaitUntil(() => qa.Find("PresetHome").IsEnabled, "Nach Stopp muss die lokale Bedienung wieder bereit sein.");
            qa.Find("PresetHome").Click();
            server.WaitFor(bytes => bytes.SequenceEqual(new byte[] { 0x81, 0x01, 0x06, 0x04, 0xff }));
            qa.Capture("positions-after-home");
            NativeQaSession.WaitUntil(() => qa.Find("PresetHome").IsEnabled, "Der Home-Auftrag muss vor dem nächsten Eingabeauftrag enden.");
            qa.Find("NavigateControl").Click();
            NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { IsEnabled: true }, "Das Steuerfeld fehlt nach Rückkehr.");
            priorStops = server.Count(IsPanStop);
            qa.Find("PanTiltPad").Focus(); Keyboard.Press(VirtualKeyShort.RIGHT);
            server.WaitForCount(bytes => bytes.Length == 9 && bytes[4] == 24 && bytes[6] == 2, 2);
            qa.Capture("main-close-held-input");
            Assert.That(qa.Application.Close(killIfCloseFails: false), Is.True, "Schließen muss den gehaltenen Auftrag stoppen und die Instanz beenden.");
            server.WaitForCount(IsPanStop, priorStops + 1);
            NativeQaSession.WaitUntil(() => server.ConnectionClosed, "Schließen muss auch die eigene TCP-Sitzung beenden.");
        }
        finally
        {
            Mouse.Up(MouseButton.Left); Keyboard.Release(VirtualKeyShort.RIGHT); Keyboard.Release(VirtualKeyShort.LEFT);
            server.Save(Path.Combine(qa.EvidenceDirectory, "wire-packets.json"));
        }
    }

    [Test]
    public void FailedStopShowsAnErrorAndDisablesPresets()
    {
        using var server = new UiViscaServer { RejectStop = true };
        using var qa = new NativeQaSession("control-stop-error", cameraPort: server.Port);
        NativeQaSession.WaitUntil(() => qa.Find("PresetHome").IsEnabled, "Die lokale QA-Verbindung fehlt.");
        qa.Find("NavigateControl").Click();
        NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PanTiltPad")) is { IsEnabled: true }, "Das Steuerfeld ist nicht bereit.");
        try
        {
            Mouse.MoveTo(PointAt(qa.Find("PanTiltPad").BoundingRectangle, .75, .25)); Mouse.Down(MouseButton.Left);
            server.WaitFor(bytes => bytes.Length == 9 && bytes[6] == 2 && bytes[7] == 1);
            Mouse.Up(MouseButton.Left);
            server.WaitFor(IsPanStop);
            NativeQaSession.WaitUntil(() => server.ConnectionClosed && !qa.Find("PanTiltPad").IsEnabled &&
                !qa.Find("ControlHome").IsEnabled && qa.Find("ControlStatus").Name.Contains("Kameraauftrag ist fehlgeschlagen", StringComparison.Ordinal),
                "Der Stoppfehler und die getrennte Sitzung müssen vollständig angezeigt sein.");
            Assert.That(qa.Find("ControlStatus").Name, Does.Not.Contain("Steuerung: Verbunden"));
            qa.Capture("stop-error");
            qa.Find("NavigatePositions").Click();
            NativeQaSession.WaitUntil(() => qa.Window.FindFirstDescendant(c => c.ByAutomationId("PresetHome")) is { IsEnabled: false }, "Die Positionsaufrufe müssen nach dem Fehler gesperrt sein.");
        }
        finally { Mouse.Up(MouseButton.Left); server.Save(Path.Combine(qa.EvidenceDirectory, "wire-packets.json")); }
    }

    private static bool IsPanStop(byte[] bytes) => bytes.Length == 9 && bytes[2] == 0x06 && bytes[6] == 3 && bytes[7] == 3;
    private static Point PointAt(RectangleF bounds, double x, double y) => new((int)(bounds.Left + bounds.Width * x), (int)(bounds.Top + bounds.Height * y));
    private static Point PointAt(Rectangle bounds, double x, double y) => new((int)(bounds.Left + bounds.Width * x), (int)(bounds.Top + bounds.Height * y));

    private sealed class UiViscaServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentQueue<byte[]> packets = new();
        private readonly Task loop;
        private string? journalPath;
        public int Port { get; }
        public bool RejectStop { get; init; }
        private int connectionClosed;
        public bool ConnectionClosed => Volatile.Read(ref connectionClosed) != 0;
        public UiViscaServer() { listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; loop = ServeAsync(); }
        public int Count(Func<byte[], bool> match) => packets.Count(match);
        public void WaitFor(Func<byte[], bool> match) => NativeQaSession.WaitUntil(() => packets.Any(match), "Das erwartete VISCA-Paket fehlt.");
        public void WaitForCount(Func<byte[], bool> match, int count) => NativeQaSession.WaitUntil(() => packets.Count(match) >= count, "Der zusätzliche Stopp fehlt.");
        public void Save(string path) { journalPath = path; File.WriteAllText(path, JsonSerializer.Serialize(packets.Select(Convert.ToHexString).ToArray())); }
        private async Task ServeAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                var pending = new List<byte>(); var buffer = new byte[64];
                while (!stop.IsCancellationRequested)
                {
                    var count = await stream.ReadAsync(buffer, stop.Token);
                    if (count == 0) { Interlocked.Exchange(ref connectionClosed, 1); return; }
                    foreach (var value in buffer.Take(count))
                    {
                        pending.Add(value); if (value != 0xff) continue;
                        var bytes = pending.ToArray(); pending.Clear(); packets.Enqueue(bytes);
                        var power = bytes.SequenceEqual(new byte[] { 0x81, 0x09, 0x04, 0x00, 0xff });
                        var response = power ? new byte[] { 0x90, 0x50, 0x02, 0xff } :
                            RejectStop && IsPanStop(bytes) ? new byte[] { 0x90, 0x60, 0x02, 0xff } : new byte[] { 0x90, 0x51, 0xff };
                        await stream.WriteAsync(response, stop.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        public void Dispose()
        {
            stop.Cancel(); listener.Stop(); loop.GetAwaiter().GetResult(); stop.Dispose();
            var stillListening = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == Port && IPAddress.IsLoopback(endpoint.Address));
            if (journalPath is not null) File.WriteAllText(Path.ChangeExtension(journalPath, ".cleanup.json"),
                JsonSerializer.Serialize(new { OwnerProcessId = Environment.ProcessId, Address = "127.0.0.1", Port, StillListening = stillListening }));
            Assert.That(stillListening, Is.False, "Der eigene Loopback-Port muss nach dem Test frei sein.");
        }
    }
}
