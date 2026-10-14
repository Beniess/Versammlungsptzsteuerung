using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CameraControl.Visca.Tests;

internal sealed class RecordingClient : IViscaClient
{
    internal List<byte[]> Requests { get; } = new();
    internal Func<ViscaPacket, CancellationToken, Task<ViscaPacket>> Reply { get; set; } =
        (_, _) => Task.FromResult(Fixtures.Response("complete"));
    internal bool IsDisposed { get; private set; }

    public Task<ViscaPacket> SendAsync(ViscaPacket request, CancellationToken cancellationToken)
    {
        Requests.Add(Fixtures.Bytes(request));
        return Reply(request, cancellationToken);
    }

    public void Dispose() => IsDisposed = true;

    internal ViscaController CreateController(TimeSpan? timeout = null) =>
        new(this, timeout ?? TimeSpan.FromSeconds(3), null);
}
