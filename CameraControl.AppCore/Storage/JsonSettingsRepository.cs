using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace CameraControl.AppCore.Storage;

public enum SettingsLoadStatus { Missing, Loaded, Invalid, UnsupportedVersion, Unavailable }
public sealed record SettingsLoadResult(SettingsLoadStatus Status, AppSettings Settings, string? Message);
public sealed class SettingsStorageException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class JsonSettingsRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };
    private readonly string dataDirectory;
    private readonly string settingsPath;
    private readonly string imagesDirectory;
    private readonly SemaphoreSlim writeLock;
    private readonly Func<Task>? beforeCommitAsync;

    public JsonSettingsRepository(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        writeLock = WriteLocks.GetOrAdd(this.dataDirectory, _ => new SemaphoreSlim(1, 1));
        settingsPath = Path.Combine(this.dataDirectory, "settings.json");
        imagesDirectory = Path.Combine(this.dataDirectory, "Images");
    }

    internal JsonSettingsRepository(string dataDirectory, Func<Task> beforeCommitAsync) : this(dataDirectory)
    {
        this.beforeCommitAsync = beforeCommitAsync;
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            byte[] json = await File.ReadAllBytesAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out JsonElement schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out int version))
                return FailedLoad(SettingsLoadStatus.Invalid, "Die Einstellungsdatei ist ungültig. Neue Einrichtung ist erforderlich.");
            if (version != AppSettings.CurrentSchemaVersion)
                return FailedLoad(SettingsLoadStatus.UnsupportedVersion, "Die Version der Einstellungsdatei wird nicht unterstützt. Die Datei bleibt unverändert.");
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null) return FailedLoad(SettingsLoadStatus.Invalid, "Die Einstellungsdatei ist ungültig.");
            SettingsValidation.Validate(settings);
            return new SettingsLoadResult(SettingsLoadStatus.Loaded, Freeze(settings), null);
        }
        catch (FileNotFoundException) { return FailedLoad(SettingsLoadStatus.Missing, null); }
        catch (DirectoryNotFoundException) { return FailedLoad(SettingsLoadStatus.Missing, null); }
        catch (JsonException) { return FailedLoad(SettingsLoadStatus.Invalid, "Die Einstellungsdatei ist ungültig. Neue Einrichtung ist erforderlich."); }
        catch (ArgumentException) { return FailedLoad(SettingsLoadStatus.Invalid, "Die Einstellungswerte sind ungültig. Die Datei bleibt unverändert."); }
        catch (IOException) { return FailedLoad(SettingsLoadStatus.Unavailable, "Die Einstellungsdatei kann nicht gelesen werden."); }
        catch (UnauthorizedAccessException) { return FailedLoad(SettingsLoadStatus.Unavailable, "Kein Lesezugriff auf die Einstellungsdatei."); }
    }

    public async Task<AppSettings> SaveAsync(AppSettings settings,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? newImages = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SettingsValidation.Validate(settings);
        AppSettings snapshot = Freeze(settings);
        var images = newImages?.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()) ?? new Dictionary<string, byte[]>();
        foreach ((string id, byte[] bytes) in images)
        {
            if (!PresetIds.Ordered.Contains(id) || !PngIntegrity.IsComplete(bytes))
                throw new ArgumentException("Ein Positionsbild hat eine ungültige ID oder ungültige PNG-Daten.", nameof(newImages));
        }
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsLoadResult existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (existing.Status is not (SettingsLoadStatus.Missing or SettingsLoadStatus.Loaded))
                throw new SettingsStorageException("Die vorhandene Einstellungsdatei kann nicht überschrieben werden. Neue Einrichtung ist erforderlich.");
            return await CommitAsync(snapshot, images, cancellationToken).ConfigureAwait(false);
        }
        finally { writeLock.Release(); }
    }

    public async Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await CommitAsync(AppSettings.CreateDefault(), new Dictionary<string, byte[]>(), cancellationToken).ConfigureAwait(false); }
        finally { writeLock.Release(); }
    }

    public async Task<byte[]?> TryReadImageAsync(string? imageFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (imageFileName is null || !SettingsValidation.IsOwnImageName(imageFileName)) return null;
        try
        {
            if (IsReparsePoint(imagesDirectory)) return null;
            string imagePath = Path.Combine(imagesDirectory, imageFileName);
            if (IsReparsePoint(imagePath)) return null;
            byte[] bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
            return PngIntegrity.IsComplete(bytes) ? bytes : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private async Task<AppSettings> CommitAsync(AppSettings settings, Dictionary<string, byte[]> images, CancellationToken cancellationToken)
    {
        string temporaryJson = Path.Combine(dataDirectory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        var createdImages = new List<string>();
        bool committed = false;
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var presets = settings.Presets.ToArray();
            if (images.Count > 0)
            {
                Directory.CreateDirectory(imagesDirectory);
                if (IsReparsePoint(imagesDirectory)) throw new IOException("Der Bilderordner ist kein eigener Datenordner.");
            }
            foreach ((string id, byte[] bytes) in images)
            {
                string name = "img-" + Guid.NewGuid().ToString("N") + ".png";
                string imagePath = Path.Combine(imagesDirectory, name);
                createdImages.Add(imagePath);
                await WriteNewAsync(imagePath, bytes, cancellationToken).ConfigureAwait(false);
                int index = Array.FindIndex(presets, preset => preset.Id == id);
                presets[index] = presets[index] with { ImageFileName = name };
            }
            AppSettings updated = settings with { Presets = Array.AsReadOnly(presets) };
            await WriteNewAsync(temporaryJson, JsonSerializer.SerializeToUtf8Bytes(updated, JsonOptions), cancellationToken).ConfigureAwait(false);
            if (beforeCommitAsync is not null) await beforeCommitAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(settingsPath)) File.Replace(temporaryJson, settingsPath, destinationBackupFileName: null);
            else File.Move(temporaryJson, settingsPath);
            committed = true;
            TryRemoveUnreferencedImages(updated);
            return updated;
        }
        catch (IOException exception) { throw new SettingsStorageException("Die Einstellungen wurden nicht gespeichert. Prüfe den Schreibzugriff auf den Datenordner.", exception); }
        catch (UnauthorizedAccessException exception) { throw new SettingsStorageException("Kein Schreibzugriff auf den Datenordner. Die Einstellungen wurden nicht gespeichert.", exception); }
        finally
        {
            TryDeleteOwnFile(temporaryJson);
            if (!committed) foreach (string image in createdImages) TryDeleteOwnFile(image);
        }
    }

    private static async Task WriteNewAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        file.Flush(flushToDisk: true);
    }

    private bool TryRemoveUnreferencedImages(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(imagesDirectory) || IsReparsePoint(imagesDirectory)) return true;
            var references = settings.Presets.Where(preset => preset.ImageFileName is not null)
                .Select(preset => preset.ImageFileName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool complete = true;
            foreach (string image in Directory.EnumerateFiles(imagesDirectory, "img-*.png"))
                if (SettingsValidation.IsOwnImageName(Path.GetFileName(image)) && !references.Contains(Path.GetFileName(image)) && !IsReparsePoint(image))
                    complete &= TryDeleteOwnFile(image);
            return complete;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool TryDeleteOwnFile(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsReparsePoint(string path) => (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static AppSettings Freeze(AppSettings settings) => settings with { Presets = Array.AsReadOnly(settings.Presets.ToArray()) };
    private static SettingsLoadResult FailedLoad(SettingsLoadStatus status, string? message) => new(status, AppSettings.CreateDefault(), message);
}

public static class WindowsDataPath
{
    public static string GetDirectory()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData)) throw new SettingsStorageException("Der Windows-Benutzerdatenordner ist nicht verfügbar.");
        return Path.Combine(localData, "Versammlungsptzsteuerung", "Data");
    }
}
