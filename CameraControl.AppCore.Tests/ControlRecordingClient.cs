using System.Collections.Concurrent;
using CameraControl.Visca;
using CameraControl.Visca.Tests;

namespace CameraControl.AppCore.Tests;

internal sealed class ControlRecordingClient : IViscaClient
{
    private readonly ConcurrentQueue<byte[]> requests = new();
    internal byte[][] Requests => requests.ToArray();
    internal bool IsDisposed { get; private set; }
    internal Func<byte[], CancellationToken, Task<ViscaPacket>>? Intercept { get; set; }
    internal ViscaController Controller(TimeSpan? timeout = null) => new(this, timeout ?? TimeSpan.FromSeconds(3), null);
    public Task<ViscaPacket> SendAsync(ViscaPacket request, CancellationToken cancellationToken)
    {
        byte[] bytes = Fixtures.Bytes(request);
        requests.Enqueue(bytes);
        if (Intercept is not null) return Intercept(bytes, cancellationToken);
        return Task.FromResult(DefaultResponse(bytes));
    }
    internal static ViscaPacket DefaultResponse(byte[] bytes) => Fixtures.Response(
        bytes.SequenceEqual(Fixtures.Wire("power-query")) ? "power-answer-on" :
        bytes.SequenceEqual(Fixtures.Wire("pan-query")) ? "pan-answer-unit" :
        bytes.SequenceEqual(Fixtures.Wire("zoom-query")) ? "zoom-answer-4660" : "complete");
    internal void Clear() { while (requests.TryDequeue(out _)) { } }
    public void Dispose() => IsDisposed = true;
}
