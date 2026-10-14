using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace CameraControl_WinUI.Deployment;

internal sealed record DeploymentMessage(string Command, string ExecutablePath, Guid Token, int Protocol = 1);

internal static class DeploymentPipeProtocol
{
    private const int MaximumMessageBytes = 8192;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static Task<DeploymentMessage> SendAsync(string pipeName, DeploymentMessage message, int expectedProcessId,
        string expectedPath, Func<int, string?> getProcessPath, TimeSpan timeout, CancellationToken cancellationToken, Action<string>? trace = null) =>
        SendCoreAsync(pipeName, message, expectedProcessId, expectedPath, getProcessPath, timeout, cancellationToken, trace);

    internal static Task<DeploymentMessage> ActivateOwnerAsync(string pipeName, string expectedPath,
        Func<int, string?> getProcessPath, TimeSpan timeout, CancellationToken cancellationToken, Action<string>? trace = null) =>
        SendCoreAsync(pipeName, new DeploymentMessage("Activate", expectedPath, Guid.Empty), null,
            expectedPath, getProcessPath, timeout, cancellationToken, trace);

    private static async Task<DeploymentMessage> SendCoreAsync(string pipeName, DeploymentMessage message, int? expectedProcessId,
        string expectedPath, Func<int, string?> getProcessPath, TimeSpan timeout, CancellationToken cancellationToken, Action<string>? trace)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var timer = Stopwatch.StartNew();
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            trace?.Invoke($"client.connected;command={message.Command};elapsedMs={timer.ElapsedMilliseconds}");
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId)) throw new Win32Exception(Marshal.GetLastWin32Error());
            bool pidMatches = processId <= int.MaxValue && (!expectedProcessId.HasValue || processId == expectedProcessId.Value);
            bool pathMatches = processId <= int.MaxValue && DeploymentScope.SamePath(getProcessPath((int)processId), expectedPath);
            trace?.Invoke($"client.identity;pidMatches={pidMatches};pathMatches={pathMatches};elapsedMs={timer.ElapsedMilliseconds}");
            if (!pidMatches || !pathMatches)
                throw new IOException("Der IPC-Endpunkt gehört nicht zum erwarteten Programmprozess.");
            await WriteAsync(pipe, message, deadline.Token).ConfigureAwait(false);
            DeploymentMessage response = await ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
            trace?.Invoke($"client.response;command={response.Command};elapsedMs={timer.ElapsedMilliseconds}");
            if (response.Protocol != 1 || !DeploymentScope.SamePath(response.ExecutablePath, expectedPath) || response.Token != message.Token)
                throw new IOException("Die IPC-Antwort gehört nicht zur aktuellen Anfrage.");
            return response;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { trace?.Invoke($"client.timeout;command={message.Command};elapsedMs={timer.ElapsedMilliseconds}"); throw new TimeoutException("Der Programmprozess hat nicht rechtzeitig auf IPC geantwortet.", error); }
        catch (Win32Exception error) { throw new IOException("Die Identität des IPC-Endpunkts konnte nicht geprüft werden.", error); }
    }

    internal static async Task WriteAsync(Stream stream, DeploymentMessage message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (payload.Length > MaximumMessageBytes) throw new IOException("Die IPC-Nachricht ist zu groß.");
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<DeploymentMessage> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is <= 0 or > MaximumMessageBytes) throw new IOException("Die IPC-Nachricht hat eine ungültige Größe.");
        byte[] payload = new byte[count];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<DeploymentMessage>(payload, Options) ?? throw new IOException("Die IPC-Nachricht ist leer."); }
        catch (JsonException error) { throw new IOException("Die IPC-Nachricht ist ungültig.", error); }
    }

    internal static int ClientProcessId(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint processId) || processId > int.MaxValue)
            throw new IOException("Die Identität des IPC-Aufrufers konnte nicht geprüft werden.");
        return (int)processId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
