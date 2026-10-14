using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CameraControl.Visca.Tests;

[TestFixture, Category("Transport"), NonParallelizable]
public sealed class TransportReferenceTests
{
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    public async Task AckAloneLeavesCommandPendingUntilCompletion()
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        var ackSent = Signal();
        var releaseCompletion = Signal();
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "home");
            await LoopbackServer.Send(stream, "ack");
            ackSent.SetResult(true);
            await LoopbackServer.Within(releaseCompletion.Task);
            await LoopbackServer.Send(stream, "complete");
        });
        try
        {
            Task command = controller.GoHome();
            await LoopbackServer.Within(ackSent.Task);
            Assert.That(await Task.WhenAny(command, Task.Delay(100)), Is.Not.SameAs(command));
            releaseCompletion.SetResult(true);
            await LoopbackServer.Within(command);
            await LoopbackServer.Within(peer);
        }
        finally { releaseCompletion.TrySetResult(true); }
    }

    [Test]
    public async Task FragmentedPanTiltAnswerPreservesSignedValues()
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "pan-query");
            foreach (byte value in Fixtures.Wire("pan-answer-unit"))
            {
                await stream.WriteAsync(new[] { value }, 0, 1);
                await Task.Delay(5);
            }
        });
        Assert.That(await LoopbackServer.Within(controller.GetPanTilt()), Is.EqualTo(((short)-1, (short)1)));
        await LoopbackServer.Within(peer);
    }

    [Test]
    public async Task AckAndCompletionInOneWriteAreConsumedSeparately()
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "home");
            await LoopbackServer.Send(stream, "ack-complete");
            await LoopbackServer.Request(stream, "zoom-query");
            await LoopbackServer.Send(stream, "zoom-answer-4660");
        });
        await LoopbackServer.Within(controller.GoHome());
        Assert.That(await LoopbackServer.Within(controller.GetZoom()), Is.EqualTo(4660));
        await LoopbackServer.Within(peer);
    }

    [TestCase("error", typeof(ViscaResponseException))]
    [TestCase("invalid-type", typeof(ViscaProtocolException))]
    [TestCase("short", typeof(ViscaProtocolException))]
    [TestCase("eof", typeof(ViscaProtocolException))]
    [TestCase("partial-eof", typeof(ViscaProtocolException))]
    public async Task FailedResponseDisconnectsAndNextCommandReconnects(string failure, Type exceptionType)
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        Task firstPeer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "zoom-query");
            if (failure == "partial-eof") await stream.WriteAsync(new byte[] { 0x90, 0x50, 0x01 }, 0, 3);
            else if (failure != "eof") await LoopbackServer.Send(stream, failure);
        });
        Assert.ThrowsAsync(exceptionType, async () => await LoopbackServer.Within(controller.GetZoom()));
        await LoopbackServer.Within(firstPeer);
        await Reconnect(server, controller);
    }

    [TestCase("incomplete-pan-answer", true)]
    [TestCase("incomplete-zoom-answer", false)]
    public async Task TruncatedCompletionPayloadDoesNotReturnPosition(string answer, bool panTilt)
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, panTilt ? "pan-query" : "zoom-query");
            await LoopbackServer.Send(stream, answer);
        });
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            if (panTilt) await LoopbackServer.Within(controller.GetPanTilt());
            else await LoopbackServer.Within(controller.GetZoom());
        });
        await LoopbackServer.Within(peer);
    }

    [Test]
    public async Task UnterminatedDataAtReadBufferLimitFailsAndReconnects()
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port, TimeSpan.FromSeconds(3));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "home");
            await stream.WriteAsync(new byte[256], 0, 256);
        });
        Assert.ThrowsAsync<ViscaProtocolException>(async () => await LoopbackServer.Within(controller.GoHome()));
        await LoopbackServer.Within(peer);
        await Reconnect(server, controller);
    }

    [TestCase(false), TestCase(true)]
    public async Task TimeoutOrCallerCancellationEndsPendingReadAndReconnects(bool callerCancellation)
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port,
            callerCancellation ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(300));
        using var cancellation = new CancellationTokenSource();
        var ackSent = Signal();
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "zoom-query");
            await LoopbackServer.Send(stream, "ack");
            ackSent.SetResult(true);
            var one = new byte[1];
            Assert.That(await LoopbackServer.Within(stream.ReadAsync(one, 0, 1)), Is.Zero);
        });
        var stopwatch = Stopwatch.StartNew();
        Task<short> operation = controller.GetZoom(cancellation.Token);
        await LoopbackServer.Within(ackSent.Task);
        if (callerCancellation) cancellation.Cancel();
        Exception failure = await CaptureReadFailure(operation);
        Assert.That(failure, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<IOException>().Or.InstanceOf<ObjectDisposedException>());
        Assert.That(operation.Status, Is.Not.EqualTo(TaskStatus.RanToCompletion));
        Assert.That(cancellation.IsCancellationRequested, Is.EqualTo(callerCancellation));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        TestContext.WriteLine((callerCancellation ? "Caller cancellation" : "Command timeout") + " produced " + failure.GetType().FullName);
        await LoopbackServer.Within(peer);
        await Reconnect(server, controller);
    }

    [Test]
    public async Task PreCancelledCommandDoesNotReachLoopbackListener()
    {
        using var server = new LoopbackServer();
        using var controller = ViscaController.ForTcp("127.0.0.1", server.Port);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await controller.GoHome(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        await Reconnect(server, controller);
    }

    private static async Task<Exception> CaptureReadFailure(Task operation)
    {
        try { await LoopbackServer.Within(operation); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception; }
        throw new AssertionException("Pending inquiry returned success after timeout/cancellation.");
    }

    private static async Task Reconnect(LoopbackServer server, ViscaController controller)
    {
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "home");
            await LoopbackServer.Send(stream, "complete");
        });
        await LoopbackServer.Within(controller.GoHome());
        await LoopbackServer.Within(peer);
    }
}
