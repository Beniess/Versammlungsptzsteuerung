namespace CameraControl.AppCore.Deployment;

public enum DeploymentState { Unconfigured, Active, Staged, Starting, Ready, Committed, Failed }
public enum DeploymentDisposition { ContinueCurrent, RedirectToActive, CandidateCommitted, Failed }

public sealed record InstalledRelease(ReleaseVersion Version, string ExecutablePath, long Length, string Sha256);
public sealed record CandidateStart(InstalledRelease Release, Guid Token);
public sealed record DeploymentResult(DeploymentDisposition Disposition, InstalledRelease? ActiveRelease,
    IReadOnlyList<DeploymentState> States, string? Error = null);

public interface IDeploymentRuntime
{
    // Include the calling process; this query must not close any process.
    Task<bool> IsRunningAsync(InstalledRelease release, CancellationToken cancellationToken);

    // Exclude the calling process; return true only after a graceful exit, or when no other instance is running.
    Task<bool> RequestExitAsync(InstalledRelease release, CancellationToken cancellationToken);

    // Start in read-only mode; return true only after the token handshake and a responsive main window.
    Task<bool> StartCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken);

    // Release the candidate's write and control restrictions after marker and link commit.
    Task CommitCandidateAsync(CandidateStart candidate, CancellationToken cancellationToken);
}

public interface IDeploymentLinks
{
    Task PrepareAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken);
    Task SwitchAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken);
    Task RestoreAsync(InstalledRelease? release, Guid operationId, CancellationToken cancellationToken);
    Task DiscardPreparedAsync(Guid operationId, CancellationToken cancellationToken);
}
