using CameraControl.AppCore;
using CameraControl.AppCore.Presets;
using NUnit.Framework;
#if HAS_APPLICATION_SERVICES
using CameraControl_WinUI;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("QaScope"), NonParallelizable]
public sealed class QaScopeTests
{
#if HAS_APPLICATION_SERVICES
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "CameraControl-WinUI-Scope-QA", Guid.NewGuid().ToString("N"));

    [Test]
    public async Task ProductionScopeDoesNotComeFromAnEnvironmentFlag()
    {
        var previous = Environment.GetEnvironmentVariable("CAMERA_CONTROL_QA_MODE");
        Environment.SetEnvironmentVariable("CAMERA_CONTROL_QA_MODE", "1");
        try
        {
            await using var services = await ApplicationServices.CreateAsync(NewDirectory(), readOnly: true);
            Assert.That(services.IsQa, Is.False);
        }
        finally { Environment.SetEnvironmentVariable("CAMERA_CONTROL_QA_MODE", previous); }
    }

    [Test]
    public async Task ExplicitQaScopeRejectsRemoteRtspBeforeOpeningThePlayer()
    {
        var directory = NewDirectory();
        try
        {
            await using var services = await ApplicationServices.CreateAsync(directory, readOnly: false, isQa: true);
            await services.SaveConnectionAsync(new ConnectionSettings(null, 5678, "rtsp://192.0.2.1/fixture", null));
            var opened = false;
            services.ConfigurePreview(new NoPhoto(), _ => { opened = true; return Task.CompletedTask; }, () => ValueTask.CompletedTask);
            Assert.ThrowsAsync<InvalidOperationException>(() => services.OpenPreviewAsync());
            Assert.That(opened, Is.False);
            Assert.That(services.IsQa, Is.True);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase("rtsp://127.0.0.1:15554/fixture")]
    [TestCase("rtsp://localhost:15554/fixture")]
    public async Task ExplicitQaScopeAllowsLocalRtsp(string url)
    {
        await using var services = await ApplicationServices.CreateAsync(NewDirectory(), readOnly: true, isQa: true);
        Assert.DoesNotThrow(() => services.ValidatePreviewEndpoint(new Uri(url)));
    }

    private sealed class NoPhoto : IPositionPhotoSource
    {
        public Task OpenAndWaitForFrameAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> CaptureValidatedPngAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
#endif
}
