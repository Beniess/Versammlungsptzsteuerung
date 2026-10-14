namespace CameraControl.AppCore.Deployment;

public sealed class LocalDeployment
{
    public const string ExecutableName = "CameraControl.WinUI.exe";
    private readonly DeploymentFiles files;
    private readonly IDeploymentRuntime runtime;
    private readonly IDeploymentLinks links;
    private readonly TimeSpan operationTimeout;
    private readonly TimeSpan candidateStartTimeout;
    private readonly TimeSpan admissionTimeout;

    public static string CurrentUserInstallationRoot => DeploymentFiles.CurrentUserRoot;

    public LocalDeployment(string installationRoot, IDeploymentRuntime runtime, IDeploymentLinks links,
        TimeSpan? operationTimeout = null, TimeSpan? candidateStartTimeout = null, TimeSpan? admissionTimeout = null)
    {
        files = new DeploymentFiles(installationRoot);
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(30);
        if (this.operationTimeout <= TimeSpan.Zero || this.operationTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        this.candidateStartTimeout = candidateStartTimeout ?? this.operationTimeout;
        if (this.candidateStartTimeout <= TimeSpan.Zero || this.candidateStartTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(candidateStartTimeout));
        this.admissionTimeout = admissionTimeout ?? TimeSpan.FromMinutes(5);
        if (this.admissionTimeout <= TimeSpan.Zero || this.admissionTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(admissionTimeout));
    }

    public async Task<DeploymentResult> StartAsync(string sourceProcessPath, ReleaseVersion version,
        CancellationToken cancellationToken = default, Action? admissionAcquired = null)
    {
        var states = new List<DeploymentState>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = files.ValidateSource(sourceProcessPath, version);
            await using FileStream lease = await files.AcquireAsync(admissionTimeout, cancellationToken).ConfigureAwait(false);
            admissionAcquired?.Invoke();
            InstalledRelease? active = await RecoverAsync(cancellationToken).ConfigureAwait(false);
            states.Add(active is null ? DeploymentState.Unconfigured : DeploymentState.Active);
            if (active is not null && version.CompareTo(active.Version) <= 0)
                return new DeploymentResult(DeploymentFiles.SamePath(source, active.ExecutablePath)
                    ? DeploymentDisposition.ContinueCurrent : DeploymentDisposition.RedirectToActive, active, states.ToArray());
            return await InstallAsync(source, version, active, states, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsOperational(error))
        {
            states.Add(DeploymentState.Failed);
            return new DeploymentResult(DeploymentDisposition.Failed, null, states.ToArray(), error.Message);
        }
    }

    private async Task<DeploymentResult> InstallAsync(string source, ReleaseVersion version, InstalledRelease? active,
        List<DeploymentState> states, CancellationToken cancellationToken)
    {
        DeploymentJournal? journal = null;
        try
        {
            InstalledRelease candidate = await files.StageAsync(source, version, cancellationToken).ConfigureAwait(false);
            journal = new DeploymentJournal(Guid.NewGuid(), DeploymentState.Staged, active, candidate);
            await files.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            states.Add(DeploymentState.Staged);
            await BoundedAsync(token => links.PrepareAsync(candidate, journal.OperationId, token), cancellationToken).ConfigureAwait(false);
            if (active is not null && !await BoundedAsync(token => runtime.RequestExitAsync(active, token), cancellationToken).ConfigureAwait(false))
                throw new IOException("Die bisherige Instanz hat sich nicht geordnet beendet.");
            journal = journal with { State = DeploymentState.Starting };
            await files.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            states.Add(DeploymentState.Starting);
            var start = new CandidateStart(candidate, Guid.NewGuid());
            if (!await BoundedAsync(token => runtime.StartCandidateAsync(start, token), cancellationToken, candidateStartTimeout).ConfigureAwait(false))
                throw new IOException("Die neue Instanz hat keinen erfolgreichen Start bestätigt.");
            await files.VerifyAsync(candidate, cancellationToken).ConfigureAwait(false);
            journal = journal with { State = DeploymentState.Ready };
            await files.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            states.Add(DeploymentState.Ready);
            await files.WriteMarkerAsync(candidate, cancellationToken).ConfigureAwait(false);
            await BoundedAsync(token => links.SwitchAsync(candidate, journal.OperationId, token), cancellationToken).ConfigureAwait(false);
            journal = journal with { State = DeploymentState.Committed };
            await files.WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            await BoundedAsync(token => runtime.CommitCandidateAsync(start, token), cancellationToken).ConfigureAwait(false);
            states.Add(DeploymentState.Committed);
            string? retirementNote = active is null ? null : await TryRetirePreviousAsync(active, candidate).ConfigureAwait(false);
            var cleanupFailures = new List<string>();
            await AttemptAsync(() => BoundedAsync(token => links.DiscardPreparedAsync(journal.OperationId, token), CancellationToken.None), cleanupFailures).ConfigureAwait(false);
            if (cleanupFailures.Count == 0)
                await AttemptAsync(() => { files.DeleteJournal(); return Task.CompletedTask; }, cleanupFailures).ConfigureAwait(false);
            return new DeploymentResult(DeploymentDisposition.CandidateCommitted, candidate, states.ToArray(),
                cleanupFailures.Count == 0 ? retirementNote : "Die Version ist aktiviert. Die Bereinigung wird beim nächsten Start fortgesetzt.");
        }
        catch (Exception error) when (IsOperational(error) || error is OperationCanceledException)
        {
            string? recoveryError = journal is null ? null : await TryRollbackAsync(journal).ConfigureAwait(false);
            if (error is OperationCanceledException) throw;
            states.Add(DeploymentState.Failed);
            return new DeploymentResult(DeploymentDisposition.Failed, active, states.ToArray(),
                recoveryError is null ? error.Message : error.Message + " " + recoveryError);
        }
    }

    private async Task<string?> TryRetirePreviousAsync(InstalledRelease previous, InstalledRelease active)
    {
        try
        {
            bool running = await BoundedAsync(token => runtime.IsRunningAsync(previous, token), CancellationToken.None).ConfigureAwait(false);
            if (running) return "Die vorherige laufende Programmversion wurde beibehalten.";
            bool removed = await BoundedAsync(token => files.RemovePreviousReleaseAsync(previous, active, token), CancellationToken.None).ConfigureAwait(false);
            return removed ? null : "Die vorherige Programmversion enthält zusätzliche Dateien und wurde beibehalten.";
        }
        catch (Exception error) when (IsOperational(error) || error is OperationCanceledException)
        { return "Die neue Version ist aktiviert. Die vorherige Programmversion wurde beibehalten. " + error.Message; }
    }

    private async Task<InstalledRelease?> RecoverAsync(CancellationToken cancellationToken)
    {
        DeploymentJournal? journal = await files.ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        InstalledRelease? marker = await files.ReadMarkerAsync(cancellationToken).ConfigureAwait(false);
        if (journal is null)
        {
            if (marker is not null) await files.VerifyAsync(marker, cancellationToken).ConfigureAwait(false);
            return marker;
        }
        if (marker != journal.Previous && marker != journal.Candidate)
            throw new InvalidDataException("Aktivierungsmarker und Einrichtungsprotokoll widersprechen sich.");
        if (journal.State == DeploymentState.Committed)
        {
            if (marker != journal.Candidate)
                throw new InvalidDataException("Der abgeschlossene Versionswechsel besitzt keinen passenden Aktivierungsmarker.");
            await files.VerifyAsync(journal.Candidate, cancellationToken).ConfigureAwait(false);
            if (!await BoundedAsync(token => runtime.RequestExitAsync(journal.Candidate, token), cancellationToken).ConfigureAwait(false))
                throw new IOException("Die unterbrochene neue Instanz hat sich nicht geordnet beendet.");
            await BoundedAsync(token => links.PrepareAsync(journal.Candidate, journal.OperationId, token), cancellationToken).ConfigureAwait(false);
            await BoundedAsync(token => links.SwitchAsync(journal.Candidate, journal.OperationId, token), cancellationToken).ConfigureAwait(false);
            await BoundedAsync(token => links.DiscardPreparedAsync(journal.OperationId, token), cancellationToken).ConfigureAwait(false);
            files.DeleteJournal();
            return journal.Candidate;
        }
        if (journal.Previous is not null)
            await files.VerifyAsync(journal.Previous, cancellationToken).ConfigureAwait(false);
        string? failure = await TryRollbackAsync(journal).ConfigureAwait(false);
        if (failure is not null) throw new IOException(failure);
        return journal.Previous;
    }

    private async Task<string?> TryRollbackAsync(DeploymentJournal journal)
    {
        var failures = new List<string>();
        await AttemptAsync(async () =>
        {
            if (journal.Previous is not null) await files.VerifyAsync(journal.Previous, CancellationToken.None).ConfigureAwait(false);
            await files.WriteMarkerAsync(journal.Previous, CancellationToken.None).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await AttemptAsync(() => BoundedAsync(token => links.RestoreAsync(journal.Previous, journal.OperationId, token), CancellationToken.None), failures).ConfigureAwait(false);
        if (journal.State != DeploymentState.Staged)
        {
            await AttemptAsync(async () =>
            {
                bool stopped = await BoundedAsync(token => runtime.RequestExitAsync(journal.Candidate, token), CancellationToken.None).ConfigureAwait(false);
                if (!stopped) throw new IOException("Die neue Instanz hat sich nicht geordnet beendet.");
            }, failures).ConfigureAwait(false);
        }
        await AttemptAsync(() => BoundedAsync(token => links.DiscardPreparedAsync(journal.OperationId, token), CancellationToken.None), failures).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            await AttemptAsync(() => { files.DeleteJournal(); return Task.CompletedTask; }, failures).ConfigureAwait(false);
        }
        else
        {
            await AttemptAsync(() => files.WriteJournalAsync(journal with { State = DeploymentState.Failed }, CancellationToken.None), failures).ConfigureAwait(false);
        }
        return failures.Count == 0 ? null : "Die Wiederherstellung ist noch nicht vollständig. " + string.Join(" ", failures);
    }

    private static async Task AttemptAsync(Func<Task> operation, List<string> failures)
    {
        try { await operation().ConfigureAwait(false); }
        catch (Exception error) when (IsOperational(error) || error is OperationCanceledException) { failures.Add(error.Message); }
    }

    private Task BoundedAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        BoundedAsync(async token => { await operation(token).ConfigureAwait(false); return true; }, cancellationToken);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, TimeSpan? maximum = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maximum ?? operationTimeout);
        try { return await operation(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Die Programmeinrichtung hat die Wartezeit überschritten.", error); }
    }

    private static bool IsOperational(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or TimeoutException;
}
