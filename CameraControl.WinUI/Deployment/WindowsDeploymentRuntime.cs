using System.ComponentModel;
using System.Diagnostics;
using CameraControl.AppCore.Deployment;

namespace CameraControl_WinUI.Deployment;

public sealed class WindowsDeploymentRuntime : IDeploymentRuntime
{
    private readonly DeploymentScope scope;
    private readonly DeploymentProcessAccess processes;
    private readonly TimeSpan timeout;
    private readonly TimeSpan coldStartTimeout;
    private readonly Action<string>? trace;
    private CandidateStart? pending;
    private int pendingProcessId;

    public WindowsDeploymentRuntime(DeploymentScope scope, Action<string>? trace = null) : this(scope, DeploymentProcessAccess.System, trace: trace) { }

    internal WindowsDeploymentRuntime(DeploymentScope scope, DeploymentProcessAccess processes, TimeSpan? timeout = null, TimeSpan? coldStartTimeout = null, Action<string>? trace = null)
    {
        this.scope = scope;
        this.processes = processes;
        this.trace = trace;
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        this.coldStartTimeout = coldStartTimeout ?? timeout ?? TimeSpan.FromSeconds(60);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (this.coldStartTimeout <= TimeSpan.Zero || this.coldStartTimeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(coldStartTimeout));
    }

    public Task<bool> IsRunningAsync(InstalledRelease release, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        scope.ValidateReleasePath(release);
        return Task.FromResult(processes.Find(release).Length != 0);
    }

    public async Task<bool> ActivateExistingAsync(InstalledRelease release, CancellationToken cancellationToken = default)
    {
        scope.ValidateReleasePath(release);
        int[] instances = processes.Find(release).Where(id => id != processes.CurrentProcessId).ToArray();
        if (instances.Length == 0) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                DeploymentMessage? response = null;
                try
                {
                    response = await DeploymentPipeProtocol.ActivateOwnerAsync(scope.PipeName(release), release.ExecutablePath,
                        processes.Path, TimeSpan.FromMilliseconds(200), deadline.Token, trace).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                if (response?.Command == "Activated") return true;
                if (response is not null && response.Command != "Starting") return false;
                if (!processes.Find(release).Any(id => id != processes.CurrentProcessId)) return false;
                await Task.Delay(40, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Die vorhandene Oberfläche ist nicht rechtzeitig bereit.", error); }
    }

    public async Task ActivateOrStartAsync(InstalledRelease release, CancellationToken cancellationToken = default)
    {
        await scope.VerifyAsync(release, cancellationToken).ConfigureAwait(false);
        if (await ActivateExistingAsync(release, cancellationToken).ConfigureAwait(false)) return;
        if (processes.Find(release).Any(id => id != processes.CurrentProcessId))
            throw new IOException("Die vorhandene Instanz hat die Aktivierung abgewiesen.");
        var start = new ProcessStartInfo(release.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(release.ExecutablePath)!
        };
        if (scope.QaRoot is not null)
        {
            start.ArgumentList.Add("--deployment-qa-root");
            start.ArgumentList.Add(scope.QaRoot);
        }
        cancellationToken.ThrowIfCancellationRequested();
        try { processes.Start(start); }
        catch (Win32Exception error) { throw new IOException("Die aktive lokale Host-EXE konnte nicht gestartet werden.", error); }
        if (!await ActivateExistingAsync(release, cancellationToken).ConfigureAwait(false))
            throw new IOException("Die aktive lokale Oberfläche konnte nicht aktiviert werden.");
    }

    public async Task<bool> RequestExitAsync(InstalledRelease release, CancellationToken cancellationToken)
    {
        scope.ValidateReleasePath(release);
        int[] instances = processes.Find(release).Where(id => id != processes.CurrentProcessId).ToArray();
        if (instances.Length == 0) return true;
        if (instances.Length != 1) return false;
        try
        {
            DeploymentMessage response = await SendAsync(release, instances[0], "Exit", Guid.Empty, cancellationToken).ConfigureAwait(false);
            if (response.Command != "Exiting") return false;
        }
        catch (IOException) { }
        catch (TimeoutException) { return false; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await processes.WaitForExit(instances[0], deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public async Task<bool> StartCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken)
    {
        if (candidate.Token == Guid.Empty) throw new IOException("Das Kandidatentoken fehlt.");
        await scope.VerifyAsync(candidate.Release, cancellationToken).ConfigureAwait(false);
        if (processes.Find(candidate.Release).Any(id => id != processes.CurrentProcessId))
            throw new IOException("Eine Instanz der Kandidatenversion ist bereits aktiv.");
        var start = new ProcessStartInfo(candidate.Release.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(candidate.Release.ExecutablePath)!
        };
        foreach (string argument in CandidateLaunch.Arguments(candidate, processes.CurrentProcessId, scope)) start.ArgumentList.Add(argument);
        cancellationToken.ThrowIfCancellationRequested();
        int processId;
        try { processId = processes.Start(start); }
        catch (Win32Exception error) { throw new IOException("Die neue lokale Host-EXE konnte nicht gestartet werden.", error); }
        trace?.Invoke($"candidate.spawned;pid={processId};version={candidate.Release.Version}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(coldStartTimeout);
        bool shellInitializationStarted = false;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (processes.Path(processId) is null) return false;
                DeploymentMessage? response = null;
                try
                {
                    response = await DeploymentPipeProtocol.SendAsync(scope.PipeName(candidate.Release),
                        new DeploymentMessage("Ready", candidate.Release.ExecutablePath, candidate.Token), processId,
                        candidate.Release.ExecutablePath, processes.Path, TimeSpan.FromMilliseconds(200), deadline.Token, trace).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                if (response?.Command == "Starting" && !shellInitializationStarted)
                {
                    shellInitializationStarted = true;
                    deadline.CancelAfter(timeout);
                }
                if (response?.Command == "Ready")
                {
                    pending = candidate;
                    pendingProcessId = processId;
                    return true;
                }
                if (response is not null && response.Command != "Starting") throw new IOException("Der Kandidat hat die Startanfrage abgewiesen.");
                await Task.Delay(40, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Die neue Oberfläche hat ihren Start nicht rechtzeitig bestätigt.", error); }
    }

    public async Task CommitCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken)
    {
        if (pending != candidate) throw new IOException("Der Kandidat besitzt keine bestätigte Startbereitschaft.");
        DeploymentMessage response = await SendAsync(candidate.Release, pendingProcessId, "Commit", candidate.Token, cancellationToken).ConfigureAwait(false);
        if (response.Command != "Committed") throw new IOException("Der Kandidat hat die Aktivierung nicht bestätigt.");
        pending = null;
    }

    private Task<DeploymentMessage> SendAsync(InstalledRelease release, int processId, string command, Guid token, CancellationToken cancellationToken) =>
        DeploymentPipeProtocol.SendAsync(scope.PipeName(release), new DeploymentMessage(command, release.ExecutablePath, token),
            processId, release.ExecutablePath, processes.Path, timeout, cancellationToken, trace);
}

internal sealed record DeploymentProcessAccess(Func<InstalledRelease, int[]> Find, Func<int, string?> Path,
    Func<ProcessStartInfo, int> Start, Func<int, CancellationToken, Task> WaitForExit, int CurrentProcessId)
{
    internal static DeploymentProcessAccess System { get; } = new(FindProcesses, ProcessPath, StartProcess, WaitForProcessExitAsync, Environment.ProcessId);

    private static int[] FindProcesses(InstalledRelease release)
    {
        var result = new List<int>();
        foreach (Process process in Process.GetProcessesByName(global::System.IO.Path.GetFileNameWithoutExtension(LocalDeployment.ExecutableName)))
        {
            using (process)
            {
                string? path = ProcessPath(process.Id);
                if (DeploymentScope.SamePath(path, release.ExecutablePath)) result.Add(process.Id);
            }
        }
        return result.ToArray();
    }

    private static string? ProcessPath(int processId)
    {
        try
        {
            if (processId == Environment.ProcessId) return Environment.ProcessPath;
            using Process process = Process.GetProcessById(processId);
            return process.HasExited ? null : process.MainModule?.FileName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception error) { throw new IOException("Der Programmprozess konnte nicht sicher zugeordnet werden.", error); }
    }

    private static int StartProcess(ProcessStartInfo start)
    {
        using Process process = Process.Start(start) ?? throw new IOException("Der neue Programmprozess wurde nicht erzeugt.");
        return process.Id;
    }

    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (process) await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
}
