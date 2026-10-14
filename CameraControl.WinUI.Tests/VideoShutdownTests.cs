using NUnit.Framework;
#if HAS_VIDEO_SHUTDOWN
using CameraControl_WinUI.Video;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("VideoShutdown")]
public sealed class VideoShutdownTests
{
#if HAS_VIDEO_SHUTDOWN
    [Test]
    public void StopFailureStillDetachesAndReleasesThenPropagates()
    {
        var steps = new List<string>();
        var stopFailure = new InvalidOperationException("stop fault");
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await VideoShutdown.RunAsync(
            () => { steps.Add("stop"); return Task.FromException(stopFailure); },
            () => steps.Add("detach"),
            () => { steps.Add("release"); return Task.CompletedTask; }));
        Assert.That(steps, Is.EqualTo(new[] { "stop", "detach", "release" }));
        Assert.That(ReferenceEquals(error, stopFailure), Is.True);
    }

    [Test]
    public void DetachFailureStillReleases()
    {
        var steps = new List<string>();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await VideoShutdown.RunAsync(
            () => { steps.Add("stop"); return Task.CompletedTask; },
            () => { steps.Add("detach"); throw new InvalidOperationException("detach fault"); },
            () => { steps.Add("release"); return Task.CompletedTask; }));
        Assert.That(steps, Is.EqualTo(new[] { "stop", "detach", "release" }));
    }

    [Test]
    public async Task NormalShutdownRunsEachStageOnceInOrder()
    {
        var steps = new List<string>();
        await VideoShutdown.RunAsync(() => { steps.Add("stop"); return Task.CompletedTask; },
            () => steps.Add("detach"), () => { steps.Add("release"); return Task.CompletedTask; });
        Assert.That(steps, Is.EqualTo(new[] { "stop", "detach", "release" }));
    }
#else
    [Test]
    public void RequiredStopFailureCleanupIsMissing() => Assert.Fail("Stopfehler muss Detach und Release trotzdem ausführen und den Fehler weitergeben.");
#endif
}
