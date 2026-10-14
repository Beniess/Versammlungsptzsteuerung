using System.Runtime.InteropServices;
using NUnit.Framework;
#if HAS_USER_OPERATION
using CameraControl_WinUI;
#endif

namespace CameraControl.WinUI.Tests;

[TestFixture, Category("UserOperation")]
public sealed class UserOperationTests
{
#if HAS_USER_OPERATION
    [TestCase("timeout")]
    [TestCase("decode")]
    public async Task PreviewAdapterFailureReturnsReadableTextInsteadOfEscapingTheUiHandler(string failure)
    {
        Exception error = failure == "timeout" ? new TimeoutException("Test wait expired") : new COMException("Test decoder failed");
        var message = await UserOperation.RunAsync(() => Task.FromException(error));
        Assert.That(message, Is.Not.Null.And.Not.Empty);
        Assert.That(message, Does.Not.Contain("Test"));
    }

    [Test]
    public async Task SuccessfulOperationReturnsNoError()
    {
        var completed = false;
        var message = await UserOperation.RunAsync(() => { completed = true; return Task.CompletedTask; });
        Assert.That(completed, Is.True);
        Assert.That(message, Is.Null);
    }
#else
    [Test] public void PreviewFailureBoundaryIsRequired() => Assert.Fail("Timeout und COM-Fehler dürfen keinen async-void UI-Handler verlassen.");
#endif
}
