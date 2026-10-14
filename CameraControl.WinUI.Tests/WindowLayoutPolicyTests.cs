using CameraControl.AppCore;
using NUnit.Framework;
#if HAS_WINDOW_LAYOUT_POLICY
using CameraControl_WinUI.Views;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture]
[Category("LayoutPolicy")]
public sealed class WindowLayoutPolicyTests
{
#if HAS_WINDOW_LAYOUT_POLICY
    [TestCase(1.0, 600, 430)]
    [TestCase(1.25, 600, 430)]
    [TestCase(1.5, 600, 430)]
    [TestCase(2.0, 720, 560)]
    public void FirstStartUsesPixelTargetAndReadableDipMinimum(double scale, int width, int height)
    {
        var bounds = WindowLayoutPolicy.Restore(null, scale, new PixelBounds(0, 0, 1920, 1040));
        Assert.That(bounds.Width, Is.EqualTo(width));
        Assert.That(bounds.Height, Is.EqualTo(height));
    }

    [TestCase(1.0, 16)]
    [TestCase(1.25, 20)]
    [TestCase(1.5, 24)]
    [TestCase(2.0, 32)]
    public void FirstStartUsesSmallTopLeftWorkAreaInset(double scale, int inset)
    {
        var bounds = WindowLayoutPolicy.Restore(null, scale, new PixelBounds(-1920, 100, 1920, 1040));
        Assert.That(bounds.X, Is.EqualTo(-1920 + inset));
        Assert.That(bounds.Y, Is.EqualTo(100 + inset));
    }

    [Test]
    public void StoredSizeUsesOriginalScaleBeforeWorkAreaClamp()
    {
        var saved = new WindowPlacement(100, 100, 600, 430, 1.0);
        var bounds = WindowLayoutPolicy.Restore(saved, 1.5, new PixelBounds(0, 0, 1920, 1040));
        Assert.That(bounds, Is.EqualTo(new PixelBounds(100, 100, 900, 645)));
    }

    [Test]
    public void RemovedMonitorReturnsWindowToAvailableWorkArea()
    {
        var saved = new WindowPlacement(3200, -600, 600, 430, 1.0);
        var bounds = WindowLayoutPolicy.Restore(saved, 1.0, new PixelBounds(0, 0, 1920, 1040));
        Assert.That(bounds.X, Is.InRange(0, 1320));
        Assert.That(bounds.Y, Is.InRange(0, 610));
        Assert.That(bounds.Width, Is.EqualTo(600));
        Assert.That(bounds.Height, Is.EqualTo(430));
    }

    [Test]
    public void SmallWorkAreaContainsTheWholeWindow()
    {
        var bounds = WindowLayoutPolicy.Restore(null, 2.0, new PixelBounds(-500, 100, 640, 480));
        Assert.That(bounds, Is.EqualTo(new PixelBounds(-500, 100, 640, 480)));
    }

    [Test]
    public void StoredUndersizedWindowGetsReadableMinimum()
    {
        var saved = new WindowPlacement(20, 20, 100, 80, 1.0);
        var bounds = WindowLayoutPolicy.Restore(saved, 1.25, new PixelBounds(0, 0, 1920, 1040));
        Assert.That(bounds.Width, Is.EqualTo(450));
        Assert.That(bounds.Height, Is.EqualTo(350));
    }
#else
    [Test]
    public void RequiredProductionWindowPolicyIsNotImplementedYet()
    {
        Assert.Fail("T5 fehlt: Views/WindowLayoutPolicy.cs muss die vorbereiteten Fensterfälle erfüllen.");
    }
#endif
}
