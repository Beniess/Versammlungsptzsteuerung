#if NETFRAMEWORK
using System;
using System.Collections.Specialized;
using System.Threading;
using CameraControl.Visca.Demo;
using NUnit.Framework;

namespace CameraControl.Visca.Tests;

[TestFixture, Category("Legacy"), Apartment(ApartmentState.STA), NonParallelizable]
public sealed class LegacyWpfTests
{
    private static MainWindow Window(ViscaController controller) => new(controller, new NameValueCollection
    {
        ["IP_Adress"] = "127.0.0.1",
        ["Port"] = "1",
        ["System.Windows.Controls.ComboBoxItem: LeserPan"] = "1",
        ["System.Windows.Controls.ComboBoxItem: LeserTilt"] = "-1",
        ["System.Windows.Controls.ComboBoxItem: LeserZoom"] = "4660"
    });

    [TestCase(0, -24, "pan-left")]
    [TestCase(0.25, -12, "pan-half-left")]
    [TestCase(0.5, 0, "pan-stop")]
    [TestCase(0.75, 12, "pan-half-right")]
    [TestCase(1, 24, "pan-right")]
    [TestCase(-0.1, -24, "pan-left")]
    [TestCase(1.1, 24, "pan-right")]
    public void HorizontalPositionUsesExistingWpfMapping(double fraction, int speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestPanTiltPosition(100, 50, 100, 100, true);
            client.Requests.Clear();
            if (speed == 24) window.TestReleasePanTilt();
            client.Requests.Clear();
            window.TestPanTiltPosition(fraction * 100, 50, 100, 100, true);
            Assert.That(window.PanSpeed, Is.EqualTo(speed));
            Assert.That(window.TiltSpeed, Is.Zero);
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
        }
        finally { window.Close(); }
    }

    [TestCase(0, 24, "tilt-up")]
    [TestCase(0.25, 12, "tilt-half-up")]
    [TestCase(0.5, 0, "pan-stop")]
    [TestCase(0.75, -12, "tilt-half-down")]
    [TestCase(1, -24, "tilt-down")]
    [TestCase(-0.1, 24, "tilt-up")]
    [TestCase(1.1, -24, "tilt-down")]
    public void VerticalPositionUsesExistingWpfMapping(double fraction, int speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestPanTiltPosition(50, 0, 100, 100, true);
            if (speed == 24) window.TestReleasePanTilt();
            client.Requests.Clear();
            window.TestPanTiltPosition(50, fraction * 100, 100, 100, true);
            Assert.That(window.PanSpeed, Is.Zero);
            Assert.That(window.TiltSpeed, Is.EqualTo(speed));
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
        }
        finally { window.Close(); }
    }

    [TestCase(0, -7, "zoom-out")]
    [TestCase(0.25, -4, "zoom-half-out")]
    [TestCase(0.5, 0, "zoom-stop")]
    [TestCase(0.75, 4, "zoom-half-in")]
    [TestCase(1, 7, "zoom-in")]
    [TestCase(-0.1, -7, "zoom-out")]
    [TestCase(1.1, 7, "zoom-in")]
    public void ZoomPositionUsesExistingWpfMapping(double fraction, int speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestZoomPosition(100, 100, true);
            if (speed == 7) window.TestReleaseZoom();
            client.Requests.Clear();
            window.TestZoomPosition(fraction * 100, 100, true);
            Assert.That(window.ZoomSpeed, Is.EqualTo(speed));
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
        }
        finally { window.Close(); }
    }

    [TestCase(0, 0, "left-up")]
    [TestCase(100, 0, "right-up")]
    [TestCase(0, 100, "left-down")]
    [TestCase(100, 100, "right-down")]
    public void DiagonalCornersUseBothAxes(double x, double y, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestPanTiltPosition(x, y, 100, 100, true);
            Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
        }
        finally { window.Close(); }
    }

    [Test]
    public void MouseReleaseAfterMovementSendsBothStops()
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestPanTiltPosition(100, 0, 100, 100, true);
            window.TestZoomPosition(100, 100, true);
            window.TestReleasePanTilt();
            window.TestReleaseZoom();
            Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("right-up"), Fixtures.Wire("zoom-in"),
                Fixtures.Wire("pan-stop"), Fixtures.Wire("zoom-stop") }));
            Assert.That(window.PanSpeed, Is.Zero);
            Assert.That(window.TiltSpeed, Is.Zero);
            Assert.That(window.ZoomSpeed, Is.Zero);
        }
        finally { window.Close(); }
    }

    [Test]
    public void ReleasedPointerAndRepeatedStateDoNotAddCommands()
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestPanTiltPosition(100, 0, 100, 100, false);
            window.TestZoomPosition(100, 100, false);
            Assert.That(client.Requests, Is.Empty);
            window.TestPanTiltPosition(100, 0, 100, 100, true);
            window.TestZoomPosition(100, 100, true);
            window.TestPanTiltPosition(100, 0, 100, 100, true);
            window.TestZoomPosition(100, 100, true);
            Assert.That(client.Requests, Has.Count.EqualTo(2));
        }
        finally { window.Close(); }
    }

    [Test]
    public void ExistingPositionCallSendsAbsoluteTargetThenZoom()
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.GoToPosition(Position.Leser);
            Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("absolute-unit"), Fixtures.Wire("zoom-4660") }));
        }
        finally { window.Close(); }
    }

    [Test]
    public void ExistingHomeSendsOnlyNativeHomeCommand()
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        MainWindow window = Window(controller);
        try
        {
            window.TestHome();
            Assert.That(client.Requests, Has.Count.EqualTo(1));
            Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire("home")));
        }
        finally { window.Close(); }
    }
}
#endif
