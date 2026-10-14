using CameraControl.AppCore.Deployment;

namespace CameraControl_WinUI.Deployment;

public sealed class WindowsDeploymentLinks(DeploymentScope scope) : IDeploymentLinks
{
    public static Task<ShellLinkInfo> InspectAsync(string path) => ShellLinkInterop.OnStaAsync(() => ShellLinkInterop.Read(path));

    public async Task PrepareAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        await scope.VerifyAsync(release, cancellationToken).ConfigureAwait(false);
        await ShellLinkInterop.OnStaAsync(() =>
        {
            EnsureCurrentOwned();
            scope.ValidateLinkPath(scope.LinkPath);
            Directory.CreateDirectory(scope.ShortcutsDirectory);
            string temporary = TemporaryPath(operationId);
            if (File.Exists(temporary))
            {
                ShellLinkInfo old = ReadOwned(temporary);
                if (!DeploymentScope.SamePath(old.TargetPath, release.ExecutablePath)) throw new IOException("Die temporäre Verknüpfung gehört zu einer anderen Version.");
                return true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            ShellLinkInterop.Write(temporary, Info(release));
            ValidatePrepared(temporary, release);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SwitchAsync(InstalledRelease release, Guid operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        await scope.VerifyAsync(release, cancellationToken).ConfigureAwait(false);
        await ShellLinkInterop.OnStaAsync(() =>
        {
            string temporary = TemporaryPath(operationId);
            ValidatePrepared(temporary, release);
            EnsureCurrentOwned();
            cancellationToken.ThrowIfCancellationRequested();
            Replace(temporary);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(InstalledRelease? release, Guid operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        if (release is not null) await scope.VerifyAsync(release, cancellationToken).ConfigureAwait(false);
        await ShellLinkInterop.OnStaAsync(() =>
        {
            EnsureCurrentOwned();
            scope.ValidateLinkPath(scope.LinkPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (release is null)
            {
                File.Delete(scope.LinkPath);
                ShellLinkInterop.Notify(scope.ShortcutsDirectory);
                return true;
            }
            Directory.CreateDirectory(scope.ShortcutsDirectory);
            string temporary = TemporaryPath(operationId, "restore");
            if (File.Exists(temporary)) ReadOwned(temporary);
            else ShellLinkInterop.Write(temporary, Info(release));
            ValidatePrepared(temporary, release);
            cancellationToken.ThrowIfCancellationRequested();
            Replace(temporary);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task DiscardPreparedAsync(Guid operationId, CancellationToken cancellationToken)
    {
        ValidateOperation(operationId);
        return ShellLinkInterop.OnStaAsync(() =>
        {
            foreach (string kind in new[] { "pending", "restore" })
            {
                string path = TemporaryPath(operationId, kind);
                if (!File.Exists(path)) continue;
                ReadOwned(path);
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(path);
            }
            return true;
        }, cancellationToken);
    }

    private void EnsureCurrentOwned()
    {
        scope.ValidateLinkPath(scope.LinkPath);
        if (Directory.Exists(scope.LinkPath)) throw new IOException("Ein Verzeichnis blockiert die Programmverknüpfung.");
        if (File.Exists(scope.LinkPath)) ReadOwned(scope.LinkPath);
    }

    private ShellLinkInfo ReadOwned(string path)
    {
        scope.ValidateLinkPath(path);
        ShellLinkInfo info = ShellLinkInterop.Read(path);
        if (info.AppUserModelId != scope.AppUserModelId || !scope.OwnsTarget(info.TargetPath) || info.Arguments != Arguments)
            throw new IOException("Eine fremde Windows-Verknüpfung wird nicht geändert.");
        return info;
    }

    private void ValidatePrepared(string path, InstalledRelease release)
    {
        if (!DeploymentScope.SamePath(ReadOwned(path).TargetPath, release.ExecutablePath))
            throw new IOException("Die vorbereitete Verknüpfung zeigt auf eine andere Version.");
    }

    private void Replace(string temporary)
    {
        scope.ValidateLinkPath(temporary);
        scope.ValidateLinkPath(scope.LinkPath);
        if (File.Exists(scope.LinkPath)) File.Replace(temporary, scope.LinkPath, destinationBackupFileName: null);
        else File.Move(temporary, scope.LinkPath);
        ShellLinkInterop.Notify(scope.ShortcutsDirectory);
    }

    private string TemporaryPath(Guid operationId, string kind = "pending")
    {
        string path = Path.Combine(scope.ShortcutsDirectory, $".CameraControl-{operationId:N}-{kind}.lnk");
        scope.ValidateLinkPath(path);
        return path;
    }

    private string Arguments => scope.QaRoot is null ? string.Empty : "--deployment-qa-root \"" + scope.QaRoot + "\"";
    private ShellLinkInfo Info(InstalledRelease release) => new(release.ExecutablePath, scope.AppUserModelId, Arguments);
    private static void ValidateOperation(Guid id)
    {
        if (id == Guid.Empty) throw new IOException("Die Verknüpfungsoperation besitzt keine gültige Kennung.");
    }
}
