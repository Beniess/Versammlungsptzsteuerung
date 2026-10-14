using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CameraControl.Visca.Tests;

[TestFixture, Category("Protocol")]
public sealed class CommandReferenceTests
{
    [Test]
    public void ExistingLibraryTargetIsUsed()
    {
        var target = (TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(ViscaController).Assembly,
            typeof(TargetFrameworkAttribute))!;
#if NETFRAMEWORK
        Assert.That(target.FrameworkName, Is.EqualTo(".NETStandard,Version=v2.0"));
#else
        Assert.That(target.FrameworkName, Is.EqualTo(".NETCoreApp,Version=v10.0"));
#endif
        TestContext.WriteLine("Loaded library: " + target.FrameworkName);
    }

    [TestCase("home"), TestCase("power-on"), TestCase("power-off")]
    public async Task FixedCommands(string name)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        switch (name)
        {
            case "home": await controller.GoHome(); break;
            case "power-on": await controller.PowerOn(); break;
            case "power-off": await controller.PowerOff(); break;
        }
        Assert.That(client.Requests, Has.Count.EqualTo(1));
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(name)));
    }

    [TestCase(1, -1, 12, "absolute-unit")]
    [TestCase(0, 0, 1, "absolute-zero")]
    [TestCase(-32768, 32767, 24, "absolute-limits")]
    [TestCase(32767, -32768, 24, "absolute-reverse-limits")]
    public async Task AbsolutePanTilt(short pan, short tilt, byte speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        await controller.SetPanTilt(pan, tilt, speed, speed);
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
    }

    [TestCase(-1, 1, 12, "relative-unit")]
    [TestCase(0, 0, 1, "relative-zero")]
    [TestCase(-32768, 32767, 24, "relative-limits")]
    public async Task RelativePanTilt(short pan, short tilt, byte speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        await controller.RelativePanTilt(pan, tilt, speed, speed);
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
    }

    [TestCase(null, null, 1, 1, "pan-stop")]
    [TestCase(true, null, 24, 1, "pan-right")]
    [TestCase(false, null, 24, 1, "pan-left")]
    [TestCase(null, true, 1, 24, "tilt-up")]
    [TestCase(null, false, 1, 24, "tilt-down")]
    [TestCase(true, true, 24, 24, "right-up")]
    [TestCase(false, true, 24, 24, "left-up")]
    [TestCase(true, false, 24, 24, "right-down")]
    [TestCase(false, false, 24, 24, "left-down")]
    public async Task ManualDirections(bool? pan, bool? tilt, byte panSpeed, byte tiltSpeed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        await controller.ContinuousPanTilt(pan, tilt, panSpeed, tiltSpeed);
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
    }

    [TestCase(null, 1, "zoom-stop")]
    [TestCase(true, 7, "zoom-in")]
    [TestCase(false, 7, "zoom-out")]
    [TestCase(true, 4, "zoom-half-in")]
    [TestCase(false, 4, "zoom-half-out")]
    public async Task ManualZoom(bool? direction, byte speed, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        await controller.ContinuousZoom(direction, speed);
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
    }

    [TestCase(4660, "zoom-4660")]
    [TestCase(0, "zoom-zero")]
    [TestCase(32767, "zoom-max")]
    [TestCase(-32768, "zoom-min-signed")]
    [TestCase(-1, "zoom-minus-one")]
    public async Task ZoomValues(short zoom, string expected)
    {
        var client = new RecordingClient();
        using var controller = client.CreateController();
        await controller.SetZoom(zoom);
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire(expected)));
    }

    [TestCase("pan-answer-unit", -1, 1)]
    [TestCase("pan-answer-zero", 0, 0)]
    [TestCase("pan-answer-limits", -32768, 32767)]
    [TestCase("pan-answer-reverse", 32767, -32768)]
    public async Task PanTiltInquiry(string answer, short expectedPan, short expectedTilt)
    {
        var client = new RecordingClient { Reply = (_, _) => Task.FromResult(Fixtures.Response(answer)) };
        using var controller = client.CreateController();
        Assert.That(await controller.GetPanTilt(), Is.EqualTo((expectedPan, expectedTilt)));
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire("pan-query")));
    }

    [TestCase("zoom-answer-4660", 4660)]
    [TestCase("zoom-answer-zero", 0)]
    [TestCase("zoom-answer-max", 32767)]
    [TestCase("zoom-answer-min", -32768)]
    [TestCase("zoom-answer-minus-one", -1)]
    public async Task ZoomInquiry(string answer, short expected)
    {
        var client = new RecordingClient { Reply = (_, _) => Task.FromResult(Fixtures.Response(answer)) };
        using var controller = client.CreateController();
        Assert.That(await controller.GetZoom(), Is.EqualTo(expected));
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire("zoom-query")));
    }

    [TestCase("power-answer-on", 2), TestCase("power-answer-off", 3)]
    public async Task PowerInquiry(string answer, int expected)
    {
        var client = new RecordingClient { Reply = (_, _) => Task.FromResult(Fixtures.Response(answer)) };
        using var controller = client.CreateController();
        Assert.That((int)await controller.GetPowerStatus(), Is.EqualTo(expected));
        Assert.That(client.Requests[0], Is.EqualTo(Fixtures.Wire("power-query")));
    }

    [TestCase("incomplete-pan-answer", true)]
    [TestCase("incomplete-zoom-answer", false)]
    public void IncompletePositionDoesNotReturnValues(string answer, bool panTilt)
    {
        var client = new RecordingClient { Reply = (_, _) => Task.FromResult(Fixtures.Response(answer)) };
        using var controller = client.CreateController();
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            if (panTilt) await controller.GetPanTilt(); else await controller.GetZoom();
        });
    }

    [Test]
    public void SpeedConstantsAndDisposeContract()
    {
        var client = new RecordingClient();
        using (client.CreateController())
        {
            Assert.That(ViscaController.MinSpeed, Is.EqualTo(1));
            Assert.That(ViscaController.MaxPanTiltSpeed, Is.EqualTo(24));
            Assert.That(ViscaController.MaxZoomSpeed, Is.EqualTo(7));
        }
        Assert.That(client.IsDisposed, Is.True);
    }

    [Test]
    public void CallerCancellationRemainsCancellation()
    {
        var client = new RecordingClient { Reply = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Fixtures.Response("complete");
        }};
        using var controller = client.CreateController();
        using var cancellation = new CancellationTokenSource();
        Task operation = controller.GoHome(cancellation.Token);
        cancellation.Cancel();
        Assert.That(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
    }
}
