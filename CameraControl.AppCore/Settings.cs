using System.Text.Json.Serialization;

namespace CameraControl.AppCore;

public sealed record CameraPosition(short Pan, short Tilt, short Zoom);

public sealed record PresetSettings(string Id, string Name, CameraPosition? Target, string? ImageFileName)
{
    [JsonIgnore] public bool IsConfigured => Id == PresetIds.Home || Target is not null;
}

public static class PresetIds
{
    public const string Leser = "leser";
    public const string Tisch = "tisch";
    public const string Redner = "redner";
    public const string Home = "home";
    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(new[] { Leser, Tisch, Redner, Home });
}

public sealed record ConnectionSettings(string? Host, int ViscaPort, string? RtspUrl, string? CredentialTarget)
{
    [JsonIgnore] public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

public sealed record WindowPlacement(double X, double Y, double Width, double Height, double Scale);
public sealed record WindowSettings(WindowPlacement? Main, WindowPlacement? Preview);

public sealed record AppSettings(int SchemaVersion, ConnectionSettings Connection,
    IReadOnlyList<PresetSettings> Presets, WindowSettings Windows)
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings CreateDefault() => new(CurrentSchemaVersion,
        new ConnectionSettings(null, 5678, null, null),
        Array.AsReadOnly(new[]
        {
            new PresetSettings(PresetIds.Leser, "Leser", null, null),
            new PresetSettings(PresetIds.Tisch, "Tisch", null, null),
            new PresetSettings(PresetIds.Redner, "Redner", null, null),
            new PresetSettings(PresetIds.Home, "Home", null, null)
        }), new WindowSettings(null, null));
}
