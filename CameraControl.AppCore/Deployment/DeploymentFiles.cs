using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameraControl.AppCore.Deployment;

internal sealed class DeploymentFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<DeploymentState>(allowIntegerValues: false) }
    };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string root;
    private string MarkerPath => Path.Combine(root, "active.json");
    private string JournalPath => Path.Combine(root, "transaction.json");

    internal DeploymentFiles(string installationRoot)
    {
        root = AbsolutePath(installationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(root, Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
            throw new ArgumentException("Das Laufwerksverzeichnis darf kein Programmordner sein.", nameof(installationRoot));
        EnsureNoReparsePoints(root);
    }

    internal static string CurrentUserRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Versammlungsptzsteuerung");

    internal string ReleasePath(ReleaseVersion version) => Path.Combine(root, "Versions", version.ToString(), LocalDeployment.ExecutableName);
    internal static bool SamePath(string first, string second) => string.Equals(first, second, PathComparison);

    internal string ValidateSource(string sourceProcessPath, ReleaseVersion version)
    {
        string fullPath = AbsolutePath(sourceProcessPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException("Der Prozesspfad muss auf die vorhandene Host-EXE zeigen.");
        EnsureNoReparsePoints(fullPath);
        if (IsWithinRoot(fullPath) && !SamePath(fullPath, ReleasePath(version)))
            throw new InvalidDataException("Der lokale Prozesspfad passt nicht zur Releaseversion.");
        return fullPath;
    }

    internal async Task<FileStream> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(root);
        Directory.CreateDirectory(root);
        string lockPath = Path.Combine(root, ".deployment.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOwnedPath(lockPath);
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 11)
            {
                if (timer.Elapsed >= timeout)
                    throw new TimeoutException("Eine andere Programmeinrichtung ist noch aktiv.", error);
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<InstalledRelease> StageAsync(string sourcePath, ReleaseVersion version, CancellationToken cancellationToken)
    {
        string destination = ReleasePath(version);
        EnsureOwnedPath(destination);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length == 0) throw new InvalidDataException("Die Host-EXE ist leer.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        var release = new InstalledRelease(version, destination, source.Length, hash);
        if (File.Exists(destination))
        {
            await VerifyAsync(release, cancellationToken).ConfigureAwait(false);
            return release;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".copy-{Guid.NewGuid():N}.tmp");
        EnsureOwnedPath(temporary);
        try
        {
            source.Position = 0;
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }
            await VerifyFileAsync(temporary, release.Length, release.Sha256, cancellationToken).ConfigureAwait(false);
            EnsureOwnedPath(destination);
            File.Move(temporary, destination);
            return release;
        }
        finally { DeleteOwnedFile(temporary); }
    }

    internal async Task<InstalledRelease?> ReadMarkerAsync(CancellationToken cancellationToken)
    {
        MarkerDocument? marker = await ReadJsonAsync<MarkerDocument>(MarkerPath, cancellationToken).ConfigureAwait(false);
        if (marker is null) return null;
        if (marker.SchemaVersion != 1 || marker.Release is null)
            throw new InvalidDataException("Der Aktivierungsmarker ist ungültig.");
        return ReadRelease(marker.Release);
    }

    internal async Task<DeploymentJournal?> ReadJournalAsync(CancellationToken cancellationToken)
    {
        JournalDocument? journal = await ReadJsonAsync<JournalDocument>(JournalPath, cancellationToken).ConfigureAwait(false);
        if (journal is null) return null;
        if (journal.SchemaVersion != 1 || journal.Candidate is null || journal.OperationId == Guid.Empty ||
            journal.State is not (DeploymentState.Staged or DeploymentState.Starting or DeploymentState.Ready or
                DeploymentState.Committed or DeploymentState.Failed))
            throw new InvalidDataException("Das Einrichtungsprotokoll ist ungültig.");
        InstalledRelease? previous = journal.Previous is null ? null : ReadRelease(journal.Previous);
        InstalledRelease candidate = ReadRelease(journal.Candidate);
        if (previous is not null && candidate.Version.CompareTo(previous.Version) <= 0)
            throw new InvalidDataException("Das Einrichtungsprotokoll enthält keinen gültigen Versionswechsel.");
        return new DeploymentJournal(journal.OperationId, journal.State, previous, candidate);
    }

    internal Task WriteMarkerAsync(InstalledRelease? release, CancellationToken cancellationToken)
    {
        if (release is null)
        {
            DeleteOwnedFile(MarkerPath);
            return Task.CompletedTask;
        }
        ValidateReleasePath(release);
        return WriteJsonAsync(MarkerPath, new MarkerDocument(1, ReleaseDocument.From(release)), cancellationToken);
    }

    internal Task WriteJournalAsync(DeploymentJournal journal, CancellationToken cancellationToken) =>
        WriteJsonAsync(JournalPath, new JournalDocument(1, journal.OperationId, journal.State,
            journal.Previous is null ? null : ReleaseDocument.From(journal.Previous), ReleaseDocument.From(journal.Candidate)), cancellationToken);

    internal void DeleteJournal() => DeleteOwnedFile(JournalPath);

    internal Task VerifyAsync(InstalledRelease release, CancellationToken cancellationToken)
    {
        ValidateReleasePath(release);
        return VerifyFileAsync(release.ExecutablePath, release.Length, release.Sha256, cancellationToken);
    }

    internal async Task<bool> RemovePreviousReleaseAsync(InstalledRelease previous, InstalledRelease active, CancellationToken cancellationToken)
    {
        ValidateReleasePath(previous);
        ValidateReleasePath(active);
        if (previous.Version.CompareTo(active.Version) >= 0 || SamePath(previous.ExecutablePath, active.ExecutablePath))
            throw new InvalidDataException("Die aktive Programmversion darf nicht bereinigt werden.");
        if (await ReadMarkerAsync(cancellationToken).ConfigureAwait(false) != active)
            throw new InvalidDataException("Der Aktivierungsmarker hat sich während der Bereinigung geändert.");
        await VerifyAsync(active, cancellationToken).ConfigureAwait(false);
        string directory = Path.GetDirectoryName(previous.ExecutablePath)!;
        if (!Directory.Exists(directory)) return true;
        string[] entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length != 1 || !SamePath(entries[0], previous.ExecutablePath)) return false;
        using (var file = new FileStream(previous.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (file.Length != previous.Length) throw new InvalidDataException("Die alte Programmdatei hat eine andere Größe.");
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, previous.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Die alte Programmdatei hat eine andere Prüfsumme.");
            cancellationToken.ThrowIfCancellationRequested();
            DeleteOwnedFile(previous.ExecutablePath);
        }
        EnsureOwnedPath(directory);
        Directory.Delete(directory, recursive: false);
        return true;
    }

    private static async Task VerifyFileAsync(string path, long expectedLength, string expectedHash, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != expectedLength)
            throw new InvalidDataException("Die Größe der Programmdatei stimmt nicht mit dem Nachweis überein.");
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die Prüfsumme der Programmdatei stimmt nicht mit dem Nachweis überein.");
    }

    private InstalledRelease ReadRelease(ReleaseDocument document)
    {
        if (document.Version is null || document.ExecutablePath is null || document.Sha256 is null || document.Length <= 0 ||
            document.Sha256.Length != 64 || document.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Der gespeicherte Programmverweis ist unvollständig.");
        ReleaseVersion version;
        try { version = ReleaseVersion.Parse(document.Version); }
        catch (FormatException error) { throw new InvalidDataException("Die gespeicherte Releaseversion ist ungültig.", error); }
        if (document.Version != version.ToString())
            throw new InvalidDataException("Die gespeicherte Releaseversion ist nicht eindeutig.");
        var release = new InstalledRelease(version, document.ExecutablePath, document.Length, document.Sha256.ToLowerInvariant());
        ValidateReleasePath(release);
        return release;
    }

    private void ValidateReleasePath(InstalledRelease release)
    {
        string path = AbsolutePath(release.ExecutablePath);
        if (!SamePath(path, ReleasePath(release.Version)))
            throw new InvalidDataException("Der Programmverweis liegt nicht am festgelegten eigenen Versionspfad.");
        EnsureOwnedPath(path);
    }

    private async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        EnsureOwnedPath(path);
        if (!File.Exists(path))
        {
            if (Directory.Exists(path)) throw new InvalidDataException("Ein Verzeichnis blockiert die Einrichtungsdatei.");
            return null;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > 32768)
            throw new InvalidDataException("Die Einrichtungsdatei hat eine ungültige Größe.");
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Die Einrichtungsdatei ist leer.");
        }
        catch (JsonException error) { throw new InvalidDataException("Die Einrichtungsdatei ist ungültig.", error); }
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        EnsureOwnedPath(path);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        EnsureOwnedPath(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            EnsureOwnedPath(path);
            if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
            else File.Move(temporary, path);
        }
        finally { DeleteOwnedFile(temporary); }
    }

    private void DeleteOwnedFile(string path)
    {
        EnsureOwnedPath(path);
        File.Delete(path);
    }

    private bool IsWithinRoot(string path) => path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private void EnsureOwnedPath(string path)
    {
        if (!IsWithinRoot(path)) throw new InvalidDataException("Der Schreibpfad liegt außerhalb des eigenen Programmordners.");
        EnsureNoReparsePoints(path);
    }

    private static string AbsolutePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
            value.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
            throw new InvalidDataException("Der Pfad muss vollständig und eindeutig sein.");
        string fullPath = Path.GetFullPath(value);
        string withoutRoot = fullPath[Path.GetPathRoot(fullPath)!.Length..];
        if (withoutRoot.Contains(':')) throw new InvalidDataException("Alternative Dateiströme sind nicht zulässig.");
        return fullPath;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string? current = path;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Verknüpfte Dateisystempfade sind für die Programmeinrichtung nicht zulässig.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    private sealed record ReleaseDocument(string? Version, string? ExecutablePath, long Length, string? Sha256)
    {
        internal static ReleaseDocument From(InstalledRelease release) =>
            new(release.Version.ToString(), release.ExecutablePath, release.Length, release.Sha256);
    }

    private sealed record MarkerDocument(int SchemaVersion, ReleaseDocument? Release);
    private sealed record JournalDocument(int SchemaVersion, Guid OperationId, DeploymentState State,
        ReleaseDocument? Previous, ReleaseDocument? Candidate);
}

internal sealed record DeploymentJournal(Guid OperationId, DeploymentState State,
    InstalledRelease? Previous, InstalledRelease Candidate);
