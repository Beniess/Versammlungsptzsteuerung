using System.IO.Pipes;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CameraControl.AppCore.Deployment;

namespace CameraControl_WinUI.Deployment;

public sealed class DeploymentPipeServer : IAsyncDisposable
{
    private readonly DeploymentScope scope;
    private readonly InstalledRelease release;
    private readonly CandidateLaunch? candidate;
    private readonly Func<CancellationToken, Task<bool>> requestExit;
    private readonly Func<CancellationToken, Task> commit;
    private readonly Func<CancellationToken, Task>? activate;
    private readonly TimeSpan requestTimeout;
    private readonly Action<string>? trace;
    private readonly CancellationTokenSource lifetime = new();
    private readonly NamedPipeServerStream pipe;
    private readonly Task loop;
    private int ready;
    private int committed;

    public DeploymentPipeServer(DeploymentScope scope, InstalledRelease release, Func<CancellationToken, Task<bool>> requestExit,
        Func<CancellationToken, Task> commit, CandidateLaunch? candidate = null, Func<CancellationToken, Task>? activate = null, Action<string>? trace = null)
        : this(scope, release, requestExit, commit, candidate,
            Environment.ProcessPath ?? throw new IOException("Der Host-Prozesspfad ist nicht verfügbar."), TimeSpan.FromSeconds(30), activate, trace) { }

    internal DeploymentPipeServer(DeploymentScope scope, InstalledRelease release, Func<CancellationToken, Task<bool>> requestExit,
        Func<CancellationToken, Task> commit, CandidateLaunch? candidate, string processPath, TimeSpan requestTimeout, Func<CancellationToken, Task>? activate = null, Action<string>? trace = null)
    {
        scope.ValidateReleasePath(release);
        if (!DeploymentScope.SamePath(DeploymentScope.Absolute(processPath), release.ExecutablePath))
            throw new IOException("Der IPC-Server läuft nicht aus der eigenen lokalen Host-EXE.");
        if (candidate is not null && (candidate.Start.Release != release || candidate.Start.Token == Guid.Empty || candidate.ParentProcessId <= 0))
            throw new IOException("Die Kandidatenidentität passt nicht zum IPC-Server.");
        this.scope = scope;
        this.release = release;
        this.candidate = candidate;
        this.requestExit = requestExit;
        this.commit = commit;
        this.activate = activate;
        this.requestTimeout = requestTimeout;
        this.trace = trace;
        committed = candidate is null ? 1 : 0;
        try
        {
            pipe = new NamedPipeServerStream(scope.PipeName(release), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        }
        catch (UnauthorizedAccessException error) { throw new IOException("Eine andere Instanz besitzt den IPC-Endpunkt bereits.", error); }
        loop = Task.Run(ListenAsync);
    }

    public bool IsReadOnly => Volatile.Read(ref committed) == 0;

    // Call only after the real main window has completed a responsive DispatcherQueue round trip.
    public void MarkShellReady()
    {
        Volatile.Write(ref ready, 1);
        trace?.Invoke($"server.ready;loop={loop.Status}");
    }

    private async Task ListenAsync()
    {
        int? lastTransportError = null;
        while (!lifetime.IsCancellationRequested)
        {
            bool accepted = false;
            try
            {
                if (!await AcceptConnectionAsync(pipe, lifetime.Token).ConfigureAwait(false)) continue;
                accepted = true;
                lastTransportError = null;
                trace?.Invoke("server.connected");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(requestTimeout);
                DeploymentMessage request = await DeploymentPipeProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
                int caller = DeploymentPipeProtocol.ClientProcessId(pipe);
                trace?.Invoke($"server.request;command={request.Command};parentMatches={candidate is null || caller == candidate.ParentProcessId};tokenMatches={candidate is null || request.Token == candidate.Start.Token};pathMatches={DeploymentScope.SamePath(request.ExecutablePath, release.ExecutablePath)}");
                string status = await HandleAsync(request, caller, deadline.Token).ConfigureAwait(false);
                trace?.Invoke($"server.response;command={status}");
                await DeploymentPipeProtocol.WriteAsync(pipe, new DeploymentMessage(status, release.ExecutablePath, request.Token), deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            {
                if (lifetime.IsCancellationRequested) break;
                if (lastTransportError != error.HResult)
                    trace?.Invoke($"server.transportError;type={error.GetType().Name};hresult={error.HResult};connectedFlag={pipe.IsConnected}");
                lastTransportError = error.HResult;
                if (!accepted) throw;
            }
            finally
            {
                if (accepted && !lifetime.IsCancellationRequested)
                {
                    trace?.Invoke($"server.disconnect;connectedFlag={pipe.IsConnected}");
                    pipe.Disconnect();
                }
            }
        }
    }

    internal static async Task<bool> AcceptConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException error) when ((error.HResult & 0xffff) == 232 && !cancellationToken.IsCancellationRequested)
        {
            // A client can leave before Accept. Managed Disconnect rejects WaitingToConnect;
            // reset the same native handle to retain the exclusive endpoint claim.
            if (!DisconnectNamedPipe(server.SafePipeHandle))
                throw new IOException("Der IPC-Endpunkt konnte nicht zurückgesetzt werden.", new Win32Exception(Marshal.GetLastWin32Error()));
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DisconnectNamedPipe(SafePipeHandle namedPipe);

    private async Task<string> HandleAsync(DeploymentMessage request, int caller, CancellationToken cancellationToken)
    {
        try { return await HandleRequestAsync(request, caller, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return request.Command == "Commit" ? "Failed" : "Refused"; }
    }

    private async Task<string> HandleRequestAsync(DeploymentMessage request, int caller, CancellationToken cancellationToken)
    {
        if (request.Protocol != 1 || !DeploymentScope.SamePath(request.ExecutablePath, release.ExecutablePath)) return "Rejected";
        if (request.Command == "Activate")
        {
            if (IsReadOnly || Volatile.Read(ref ready) == 0) return "Starting";
            if (activate is null) return "Refused";
            await activate(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            return "Activated";
        }
        if (request.Command == "Exit") return await requestExit(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false) ? "Exiting" : "Refused";
        if (candidate is null || request.Token != candidate.Start.Token || caller != candidate.ParentProcessId) return "Rejected";
        if (request.Command == "Ready") return Volatile.Read(ref ready) == 1 ? "Ready" : "Starting";
        if (request.Command != "Commit" || Volatile.Read(ref ready) == 0) return "Rejected";
        if (Volatile.Read(ref committed) == 1) return "Committed";
        if (!await scope.IsActiveAsync(release, cancellationToken).ConfigureAwait(false)) return "Rejected";
        await scope.VerifyAsync(release, cancellationToken).ConfigureAwait(false);
        await commit(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref committed, 1);
        return "Committed";
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        pipe.Dispose();
        try { await loop.ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        lifetime.Dispose();
    }
}
