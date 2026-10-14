using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CameraControl.AppCore.Deployment;

namespace CameraControl_WinUI.Deployment;

public sealed class DeploymentScope
{
    public const string ProductAppUserModelId = "Versammlungsptzsteuerung.CameraControl";
    public const string ShortcutName = "Versammlungs-Kamerasteuerung.lnk";
    public string InstallationRoot { get; }
    public string ShortcutsDirectory { get; }
    public string AppUserModelId { get; }
    public string? QaRoot { get; }
    public string LinkPath => Path.Combine(ShortcutsDirectory, ShortcutName);

    private DeploymentScope(string installationRoot, string shortcutsDirectory, string appUserModelId, string? qaRoot)
    {
        InstallationRoot = Absolute(installationRoot);
        ShortcutsDirectory = Absolute(shortcutsDirectory);
        AppUserModelId = appUserModelId;
        QaRoot = qaRoot;
        CheckPath(InstallationRoot);
        CheckPath(ShortcutsDirectory);
    }

    public static DeploymentScope ForCurrentUser() => new(LocalDeployment.CurrentUserInstallationRoot,
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductAppUserModelId, null);

    public static DeploymentScope ForQa(string testRoot)
    {
        string root = Absolute(testRoot);
        string temporary = Absolute(Path.GetTempPath());
        if (!IsWithin(root, temporary)) throw new IOException("Der QA-Pfad muss unter dem temporären Benutzerordner liegen.");
        return new DeploymentScope(Path.Combine(root, "Programs", "Versammlungsptzsteuerung.QA"), Path.Combine(root, "Menu"),
            "Versammlungsptzsteuerung.QA." + Hash(root)[..24], root);
    }

    internal string PipeName(InstalledRelease release)
    {
        ValidateReleasePath(release);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string user = identity.User?.Value ?? throw new IOException("Das Windows-Benutzerkonto ist nicht verfügbar.");
        return "Versammlungsptzsteuerung." + Hash(user + "|" + InstallationRoot.ToUpperInvariant() + "|" + release.Version)[..48];
    }

    internal string ReleasePath(ReleaseVersion version) => Path.Combine(InstallationRoot, "Versions", version.ToString(), LocalDeployment.ExecutableName);

    internal void ValidateReleasePath(InstalledRelease release)
    {
        if (!SamePath(Absolute(release.ExecutablePath), ReleasePath(release.Version)))
            throw new IOException("Der Programmverweis liegt nicht im eigenen Versionsordner.");
        CheckPath(release.ExecutablePath);
    }

    internal async Task VerifyAsync(InstalledRelease release, CancellationToken cancellationToken)
    {
        ValidateReleasePath(release);
        using var file = new FileStream(release.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (file.Length <= 0 || file.Length != release.Length) throw new IOException("Die Programmdatei hat eine unerwartete Größe.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Die Programmprüfsumme ist ungültig.");
    }

    internal async Task<bool> IsActiveAsync(InstalledRelease release, CancellationToken cancellationToken)
    {
        string path = Path.Combine(InstallationRoot, "active.json");
        CheckPath(path);
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > 32768) return false;
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("schemaVersion", out JsonElement schema) && schema.TryGetInt32(out int value) && value == 1 &&
                root.TryGetProperty("release", out JsonElement item) && item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("version", out JsonElement version) && version.GetString() == release.Version.ToString() &&
                item.TryGetProperty("executablePath", out JsonElement executable) && SamePath(executable.GetString(), release.ExecutablePath) &&
                item.TryGetProperty("length", out JsonElement length) && length.TryGetInt64(out long bytes) && bytes == release.Length &&
                item.TryGetProperty("sha256", out JsonElement hash) && string.Equals(hash.GetString(), release.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal void ValidateLinkPath(string path)
    {
        if (!IsWithin(Absolute(path), ShortcutsDirectory)) throw new IOException("Der Linkpfad liegt außerhalb des eigenen Linkverzeichnisses.");
        CheckPath(path);
    }

    internal bool OwnsTarget(string target)
    {
        try
        {
            string path = Absolute(target);
            string? directory = Path.GetDirectoryName(path);
            ReleaseVersion version = ReleaseVersion.Parse(Path.GetFileName(directory) ?? string.Empty);
            return SamePath(path, ReleasePath(version));
        }
        catch (Exception error) when (error is IOException or ArgumentException or FormatException) { return false; }
    }

    internal static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
            throw new IOException("Der Dateipfad ist nicht vollständig oder eindeutig.");
        string result = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? drive = Path.GetPathRoot(result);
        if (drive is null || result.Length <= drive.Length || result[drive.Length..].Contains(':'))
            throw new IOException("Der Dateipfad ist nicht zulässig.");
        return result;
    }

    internal static void CheckPath(string path)
    {
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Verknüpfte Dateisystempfade sind nicht zulässig.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static bool SamePath(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsWithin(string path, string root) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record CandidateLaunch(CandidateStart Start, int ParentProcessId)
{
    internal static IEnumerable<string> Arguments(CandidateStart start, int parentProcessId, DeploymentScope scope)
    {
        yield return "--deployment-candidate";
        yield return start.Token.ToString("N");
        yield return "--deployment-version";
        yield return start.Release.Version.ToString();
        yield return "--deployment-parent";
        yield return parentProcessId.ToString(CultureInfo.InvariantCulture);
        if (scope.QaRoot is not null)
        {
            yield return "--deployment-qa-root";
            yield return scope.QaRoot;
        }
    }

    public static async Task<CandidateLaunch?> ParseAsync(string[] arguments, DeploymentScope scope, string processPath,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.Contains("--deployment-candidate", StringComparer.Ordinal)) return null;
        try
        {
            if (arguments.Length is not (6 or 8)) throw new IOException("Die Kandidatenargumente sind unvollständig.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < arguments.Length; index += 2)
            {
                string key = arguments[index];
                if (key is not ("--deployment-candidate" or "--deployment-version" or "--deployment-parent" or "--deployment-qa-root") ||
                    !values.TryAdd(key, arguments[index + 1])) throw new IOException("Die Kandidatenargumente sind nicht eindeutig.");
            }
            if (!values.TryGetValue("--deployment-candidate", out string? tokenText) || !Guid.TryParseExact(tokenText, "N", out Guid token) || token == Guid.Empty ||
                !values.TryGetValue("--deployment-version", out string? versionText) ||
                !values.TryGetValue("--deployment-parent", out string? parentText) ||
                !int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out int parent) || parent <= 0)
                throw new IOException("Die Kandidatenidentität ist ungültig.");
            values.TryGetValue("--deployment-qa-root", out string? qaRoot);
            if (!DeploymentScope.SamePath(qaRoot, scope.QaRoot)) throw new IOException("Der Kandidat verwendet einen anderen Programmbereich.");
            ReleaseVersion version = ReleaseVersion.Parse(versionText);
            string path = DeploymentScope.Absolute(processPath);
            if (!DeploymentScope.SamePath(path, scope.ReleasePath(version))) throw new IOException("Der Kandidat läuft nicht aus der lokalen Host-EXE.");
            DeploymentScope.CheckPath(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (file.Length <= 0) throw new IOException("Die Host-EXE ist leer.");
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            return new CandidateLaunch(new CandidateStart(new InstalledRelease(version, path, file.Length, hash), token), parent);
        }
        catch (FormatException error) { throw new IOException("Die Kandidatenversion ist ungültig.", error); }
    }
}
