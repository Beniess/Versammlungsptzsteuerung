using CameraControl.AppCore.Control;
using CameraControl.Visca;
using CameraControl.Visca.Tests;
using NUnit.Framework;

namespace CameraControl.AppCore.Tests;

[TestFixture, Category("Controls"), NonParallelizable]
public sealed class ControlsTests
{
    private static ConnectionSettings Connection => new("127.0.0.1", 52381, null, null);
    private static PresetSettings Position(string id = PresetIds.Leser) => new(id, "Position", new CameraPosition(1, -1, 4660), null);
    private static CameraSession Session(ControlRecordingClient client, TimeSpan? timeout = null) => new(_ => client.Controller(timeout));
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase(0, -24, "pan-left"), TestCase(0.25, -12, "pan-half-left"), TestCase(0.5, 0, "pan-stop")]
    [TestCase(0.75, 12, "pan-half-right"), TestCase(1, 24, "pan-right")]
    [TestCase(-0.1, -24, "pan-left"), TestCase(1.1, 24, "pan-right")]
    public async Task HorizontalMappingMatchesFixedBaseline(double fraction, int speed, string expected)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        await session.SetPanTiltAsync(24, 0);
        if (speed == 24) await session.StopAsync();
        client.Clear();
        PanTiltSpeed mapped = ManualControlMap.PanTilt(fraction * 100, 50, 100, 100);
        Assert.That(mapped, Is.EqualTo(new PanTiltSpeed(speed, 0)));
        await session.SetPanTiltAsync(mapped.Pan, mapped.Tilt);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire(expected) }));
    }

    [TestCase(0, 24, "tilt-up"), TestCase(0.25, 12, "tilt-half-up"), TestCase(0.5, 0, "pan-stop")]
    [TestCase(0.75, -12, "tilt-half-down"), TestCase(1, -24, "tilt-down")]
    [TestCase(-0.1, 24, "tilt-up"), TestCase(1.1, -24, "tilt-down")]
    public async Task VerticalMappingMatchesFixedBaseline(double fraction, int speed, string expected)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        await session.SetPanTiltAsync(0, 24);
        if (speed == 24) await session.StopAsync();
        client.Clear();
        PanTiltSpeed mapped = ManualControlMap.PanTilt(50, fraction * 100, 100, 100);
        Assert.That(mapped, Is.EqualTo(new PanTiltSpeed(0, speed)));
        await session.SetPanTiltAsync(mapped.Pan, mapped.Tilt);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire(expected) }));
    }

    [TestCase(0, -7, "zoom-out"), TestCase(0.25, -4, "zoom-half-out"), TestCase(0.5, 0, "zoom-stop")]
    [TestCase(0.75, 4, "zoom-half-in"), TestCase(1, 7, "zoom-in")]
    [TestCase(-0.1, -7, "zoom-out"), TestCase(1.1, 7, "zoom-in")]
    public async Task ZoomMappingMatchesFixedBaseline(double fraction, int speed, string expected)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        await session.SetZoomAsync(7);
        if (speed == 7) await session.StopAsync();
        client.Clear();
        int mapped = ManualControlMap.Zoom(fraction * 100, 100);
        Assert.That(mapped, Is.EqualTo(speed));
        await session.SetZoomAsync(mapped);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire(expected) }));
    }

    [TestCase(0, 0, "left-up"), TestCase(100, 0, "right-up")]
    [TestCase(0, 100, "left-down"), TestCase(100, 100, "right-down")]
    public async Task DiagonalsMatchFixedBaseline(double x, double y, string expected)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        PanTiltSpeed speed = ManualControlMap.PanTilt(x, y, 100, 100);
        await session.SetPanTiltAsync(speed.Pan, speed.Tilt);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire(expected) }));
    }

    [Test]
    public async Task ConnectionUsesConfiguredEndpointAndWaitsForResponse()
    {
        var client = new ControlRecordingClient();
        var release = Signal();
        ConnectionSettings? received = null;
        await using var session = new CameraSession(settings => { received = settings; return client.Controller(); });
        client.Intercept = async (bytes, token) => { await release.Task.WaitAsync(token); return ControlRecordingClient.DefaultResponse(bytes); };
        Task connect = session.ConnectAsync(Connection);
        try
        {
            Assert.That(session.IsBusy, Is.True);
            Assert.That(session.IsConnected, Is.False);
            Assert.That(received, Is.EqualTo(Connection));
            Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("power-query") }));
        }
        finally { release.TrySetResult(true); }
        await connect;
        Assert.That(session.IsConnected, Is.True);
        Assert.That(session.IsBusy, Is.False);
    }

    [Test]
    public async Task ReadPositionUsesOnePanTiltInquiryAndOneZoomInquiry()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        Assert.That(await session.ReadPositionAsync(), Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-query"), Fixtures.Wire("zoom-query") }));
    }

    [TestCase(PresetIds.Leser), TestCase(PresetIds.Tisch), TestCase(PresetIds.Redner)]
    public async Task PresetSendsAbsoluteTwelveTwelveThenZoom(string id)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        Assert.That(await session.RecallAsync(Position(id)), Is.True);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("absolute-unit"), Fixtures.Wire("zoom-4660") }));
    }

    [Test]
    public async Task HomeSendsOnlyNativeHomeAndEmptyPresetSendsNothing()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        Assert.That(await session.RecallAsync(AppSettings.CreateDefault().Presets[0]), Is.False);
        Assert.That(client.Requests, Is.Empty);
        await session.ConnectAsync(Connection);
        client.Clear();
        Assert.That(await session.RecallAsync(AppSettings.CreateDefault().Presets[3]), Is.True);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("home") }));
    }

    [Test]
    public async Task ReleaseStopsBothAxesAndRepeatedStatesAreDeduplicated()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        await session.SetPanTiltAsync(24, 24);
        await session.SetPanTiltAsync(24, 24);
        await session.SetZoomAsync(7);
        await session.SetZoomAsync(7);
        Assert.That(session.IsMoving, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPositionAsync());
        await session.StopAsync();
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("right-up"), Fixtures.Wire("zoom-in"), Fixtures.Wire("pan-stop"), Fixtures.Wire("zoom-stop") }));
        Assert.That(session.IsMoving, Is.False);
        Assert.That(session.PanSpeed, Is.Zero);
        Assert.That(session.TiltSpeed, Is.Zero);
        Assert.That(session.ZoomSpeed, Is.Zero);
    }

    [Test]
    public async Task FastInputKeepsOnlyLatestPendingState()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        var sent = Signal();
        var release = Signal();
        client.Intercept = async (bytes, token) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("pan-right"))) { sent.TrySetResult(true); await release.Task.WaitAsync(token); }
            return ControlRecordingClient.DefaultResponse(bytes);
        };
        Task first = session.SetPanTiltAsync(24, 0);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task[] pending = Enumerable.Range(0, 100).Select(index => session.SetPanTiltAsync(index == 99 ? -24 : index % 2 == 0 ? -12 : 12, 0)).ToArray();
        Assert.That(pending.Distinct().Count(), Is.EqualTo(1));
        release.TrySetResult(true);
        await first;
        await Task.WhenAll(pending);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-right"), Fixtures.Wire("pan-left") }));
    }

    [Test]
    public async Task StopDiscardsPendingMovementAndBlocksNewMovementUntilCompletion()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        var sent = Signal();
        var release = Signal();
        client.Intercept = async (bytes, token) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("pan-right"))) { sent.TrySetResult(true); await release.Task.WaitAsync(token); }
            return ControlRecordingClient.DefaultResponse(bytes);
        };
        Task first = session.SetPanTiltAsync(24, 0);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task pending = session.SetPanTiltAsync(-24, -24);
        Task zoom = session.SetZoomAsync(7);
        Task stop = session.StopAsync();
        try
        {
            Assert.That(stop.IsCompleted, Is.False);
            Assert.That(session.IsBusy, Is.True);
            Assert.Throws<InvalidOperationException>(() => session.SetPanTiltAsync(24, 24));
        }
        finally { release.TrySetResult(true); }
        await Task.WhenAll(first, pending, zoom, stop);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-right"), Fixtures.Wire("pan-stop") }));
        Assert.That(session.IsMoving, Is.False);
    }

    [Test]
    public async Task SecondPresetIsRejectedWithoutQueuing()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        var sent = Signal();
        var release = Signal();
        client.Intercept = async (bytes, token) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("absolute-unit"))) { sent.TrySetResult(true); await release.Task.WaitAsync(token); }
            return ControlRecordingClient.DefaultResponse(bytes);
        };
        Task<bool> first = session.RecallAsync(Position());
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(await session.RecallAsync(Position(PresetIds.Tisch)), Is.False);
        Assert.That(session.IsBusy, Is.True);
        release.TrySetResult(true);
        Assert.That(await first, Is.True);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("absolute-unit"), Fixtures.Wire("zoom-4660") }));
    }

    [TestCase(false), TestCase(true)]
    public async Task FailedOrCancelledPositionDoesNotSignalSuccess(bool cancel)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        using var cancellation = new CancellationTokenSource();
        client.Intercept = async (_, token) =>
        {
            if (!cancel) throw new ViscaResponseException("Fixed device error");
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Fixtures.Response("complete");
        };
        if (cancel) Assert.That(async () => await session.RecallAsync(Position(), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        else Assert.ThrowsAsync<CameraOperationException>(async () => await session.RecallAsync(Position()));
        Assert.That(client.Requests, Has.Length.EqualTo(1));
        Assert.That(session.IsConnected, Is.False);
        Assert.That(session.IsBusy, Is.False);
        Assert.That(client.IsDisposed, Is.True);
    }

    [Test]
    public async Task DisconnectedMovementAndStopCannotReturnSuccess()
    {
        await using var session = new CameraSession();
        Assert.Throws<InvalidOperationException>(() => session.SetPanTiltAsync(24, 0));
        Assert.Throws<InvalidOperationException>(() => session.SetZoomAsync(7));
        Assert.Throws<InvalidOperationException>(() => session.StopAsync());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ConnectAsync(AppSettings.CreateDefault().Connection));
    }

    [Test]
    public async Task ReconnectDisposesOldControllerAndChangedReportsState()
    {
        var first = new ControlRecordingClient();
        var second = new ControlRecordingClient();
        var clients = new Queue<ControlRecordingClient>(new[] { first, second });
        await using var session = new CameraSession(_ => clients.Dequeue().Controller());
        int changed = 0;
        session.Changed += (_, _) => Interlocked.Increment(ref changed);
        await session.ConnectAsync(Connection);
        await session.SetPanTiltAsync(24, 0);
        await session.ConnectAsync(Connection with { ViscaPort = 12345 });
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(first.Requests.Last(), Is.EqualTo(Fixtures.Wire("pan-stop")));
        Assert.That(second.Requests, Is.EqualTo(new[] { Fixtures.Wire("power-query") }));
        Assert.That(session.IsMoving, Is.False);
        await session.DisconnectAsync();
        Assert.That(second.IsDisposed, Is.True);
        Assert.That(session.IsConnected, Is.False);
        Assert.That(changed, Is.GreaterThan(3));
    }

    [Test]
    public async Task RealLoopbackSessionUsesFixedCommandsAndDisconnects()
    {
        using var server = new LoopbackServer();
        await using var session = new CameraSession(TimeSpan.FromSeconds(3));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "power-query");
            await LoopbackServer.Send(stream, "power-answer-on");
            await LoopbackServer.Request(stream, "absolute-unit");
            await LoopbackServer.Send(stream, "ack-complete");
            await LoopbackServer.Request(stream, "zoom-4660");
            await LoopbackServer.Send(stream, "complete");
            await LoopbackServer.Request(stream, "home");
            await LoopbackServer.Send(stream, "complete");
            await LoopbackServer.Request(stream, "pan-query");
            await LoopbackServer.Send(stream, "pan-answer-unit");
            await LoopbackServer.Request(stream, "zoom-query");
            await LoopbackServer.Send(stream, "zoom-answer-4660");
            var one = new byte[1];
            Assert.That(await LoopbackServer.Within(stream.ReadAsync(one, 0, 1)), Is.Zero);
        });
        await session.ConnectAsync(Connection with { ViscaPort = server.Port });
        Assert.That(await session.RecallAsync(Position()), Is.True);
        Assert.That(await session.RecallAsync(AppSettings.CreateDefault().Presets[3]), Is.True);
        Assert.That(await session.ReadPositionAsync(), Is.EqualTo(new CameraPosition(-1, 1, 4660)));
        await session.DisconnectAsync();
        await LoopbackServer.Within(peer);
    }

    [TestCase(false), TestCase(true)]
    public async Task LoopbackDeviceErrorOrTimeoutDoesNotConnect(bool timeout)
    {
        using var server = new LoopbackServer();
        await using var session = new CameraSession(TimeSpan.FromMilliseconds(200));
        Task peer = server.Session(async stream =>
        {
            await LoopbackServer.Request(stream, "power-query");
            if (!timeout) await LoopbackServer.Send(stream, "error");
            else
            {
                var one = new byte[1];
                Assert.That(await LoopbackServer.Within(stream.ReadAsync(one, 0, 1)), Is.Zero);
            }
        });
        Assert.ThrowsAsync<CameraOperationException>(async () => await session.ConnectAsync(Connection with { ViscaPort = server.Port }));
        Assert.That(session.IsConnected, Is.False);
        Assert.That(session.IsBusy, Is.False);
        Assert.That(session.LastError, Is.Not.Empty);
        await LoopbackServer.Within(peer);
    }

    [TestCase(false), TestCase(true)]
    public async Task FailedOrCancelledStopDoesNotReportSuccess(bool cancel)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        await session.SetPanTiltAsync(24, 0);
        client.Clear();
        using var cancellation = new CancellationTokenSource();
        client.Intercept = async (bytes, token) =>
        {
            Assert.That(bytes, Is.EqualTo(Fixtures.Wire("pan-stop")));
            if (!cancel) throw new ViscaResponseException("Fixed stop error");
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Fixtures.Response("complete");
        };
        if (cancel) Assert.That(async () => await session.StopAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        else Assert.ThrowsAsync<CameraOperationException>(async () => await session.StopAsync());
        Assert.That(session.IsConnected, Is.False);
        Assert.That(session.IsBusy, Is.False);
        Assert.That(session.LastError, Is.Not.Empty);
        Assert.That(client.IsDisposed, Is.True);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-stop") }));
    }

    [Test]
    public async Task LatestInputReturningToActiveStateRemovesOldPendingMovement()
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        var sent = Signal();
        var release = Signal();
        client.Intercept = async (bytes, token) =>
        {
            if (bytes.SequenceEqual(Fixtures.Wire("pan-right"))) { sent.TrySetResult(true); await release.Task.WaitAsync(token); }
            return ControlRecordingClient.DefaultResponse(bytes);
        };
        Task first = session.SetPanTiltAsync(24, 0);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task oldPending = session.SetPanTiltAsync(-24, 0);
        Task latest = session.SetPanTiltAsync(24, 0);
        release.TrySetResult(true);
        await Task.WhenAll(first, oldPending, latest);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire("pan-right") }));
        Assert.That(session.PanSpeed, Is.EqualTo(24));
    }

    [TestCase(false), TestCase(true)]
    public async Task ClosingCancelsAndAwaitsExclusiveOperationBeforeDisconnect(bool readPosition)
    {
        var client = new ControlRecordingClient();
        await using var session = Session(client);
        await session.ConnectAsync(Connection);
        client.Clear();
        using var closing = new CancellationTokenSource();
        var sent = Signal();
        client.Intercept = async (_, token) =>
        {
            sent.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            return Fixtures.Response("complete");
        };
        Task operation = readPosition ? session.ReadPositionAsync(closing.Token) : session.RecallAsync(Position(), closing.Token);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(session.IsBusy, Is.True);
        closing.Cancel();
        Assert.That(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(session.IsBusy, Is.False);
        Assert.That(session.IsConnected, Is.False);
        Assert.Throws<InvalidOperationException>(() => session.StopAsync());
        await session.DisconnectAsync();
        Assert.That(client.IsDisposed, Is.True);
        Assert.That(client.Requests, Is.EqualTo(new[] { Fixtures.Wire(readPosition ? "pan-query" : "absolute-unit") }));
        TestContext.WriteLine("Closing sequence: caller cancelled -> exclusive operation awaited -> disconnected controller -> Disconnect completed.");
    }
}
