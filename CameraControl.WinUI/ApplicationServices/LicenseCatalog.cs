using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameraControl_WinUI;

public sealed record LicenseFile(string Path, string Sha256, bool ReadableText,
    string? SourceArchive = null, string? SourceEntry = null, string? Extraction = null);
public sealed record LicenseComponent(string Name, string Version, string License, string Copyright,
    string Source, IReadOnlyList<LicenseFile> Files, string? Scope = null);

public sealed class LicenseCatalog
{
    private const string Prefix = "CameraControl.Licenses/";
    private readonly Assembly assembly;
    private readonly Dictionary<string, string> resources;
    public IReadOnlyList<LicenseComponent> Components { get; }
    public string NoticesPath { get; }
    private sealed record Index(int FormatVersion, IReadOnlyList<LicenseComponent> Components, string Notices);

    private LicenseCatalog(Assembly assembly)
    {
        this.assembly = assembly;
        resources = assembly.GetManifestResourceNames()
            .Select(name => (Original: name, Normalized: name.Replace('\\', '/')))
            .Where(item => item.Normalized.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(item => item.Normalized[Prefix.Length..], item => item.Original, StringComparer.Ordinal);
        Index index;
        try
        {
            index = JsonSerializer.Deserialize<Index>(ReadOriginal("index.json"), new JsonSerializerOptions
            {
                RespectRequiredConstructorParameters = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Der Lizenzkatalog ist leer.");
        }
        catch (JsonException error) { throw new InvalidDataException("Der Lizenzkatalog kann nicht gelesen werden.", error); }
        if (index.FormatVersion != 1 || index.Components is not { Count: > 0 } || string.IsNullOrWhiteSpace(index.Notices))
            throw new InvalidDataException("Die Version oder der Inhalt des Lizenzkatalogs ist ungültig.");
        Components = Array.AsReadOnly(index.Components.ToArray());
        NoticesPath = index.Notices;
        foreach (var component in Components)
        {
            if (string.IsNullOrWhiteSpace(component.Name) || component.Files is not { Count: > 0 })
                throw new InvalidDataException("Eine Lizenzkomponente ist unvollständig.");
            foreach (var file in component.Files)
            {
                var hash = Convert.ToHexString(SHA256.HashData(ReadOriginal(file.Path)));
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Ein eingebetteter Lizenztext hat eine ungültige Prüfsumme.");
            }
        }
        _ = ReadText(NoticesPath);
        _ = ReadText("Sources-and-replacement.md");
    }

    public static LicenseCatalog Load(Assembly? assembly = null) => new(assembly ?? typeof(LicenseCatalog).Assembly);

    public byte[] ReadOriginal(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(part => part is "" or "." or "..") || !resources.TryGetValue(normalized, out var name))
            throw new InvalidDataException("Die angeforderte Lizenzressource ist nicht enthalten.");
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Eine Lizenzressource kann nicht geöffnet werden.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public string ReadText(string path)
    {
        var bytes = ReadOriginal(path);
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Die Lizenzressource ist kein lesbarer UTF-8-Text.", error); }
    }
}
