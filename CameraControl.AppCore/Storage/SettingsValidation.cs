using System.Net;
using System.Text.RegularExpressions;

namespace CameraControl.AppCore.Storage;

internal static class SettingsValidation
{
    private static readonly Regex OwnImageName = new("^img-[0-9a-f]{32}\\.png$", RegexOptions.CultureInvariant);
    internal static bool IsOwnImageName(string value) => OwnImageName.IsMatch(value);

    internal static void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
            throw new ArgumentException("Die Einstellungsversion wird nicht unterstützt.", nameof(settings));
        if (settings.Connection is null || settings.Presets is null || settings.Windows is null)
            throw new ArgumentException("Die Einstellungen sind unvollständig.", nameof(settings));
        ConnectionSettings connection = settings.Connection;
        if (connection.ViscaPort is < 1 or > 65535)
            throw new ArgumentException("Der VISCA-Port muss zwischen 1 und 65535 liegen.", nameof(settings));
        if (connection.Host is not null && (string.IsNullOrWhiteSpace(connection.Host) || connection.Host != connection.Host.Trim() ||
            (!IPAddress.TryParse(connection.Host, out _) && Uri.CheckHostName(connection.Host) != UriHostNameType.Dns)))
            throw new ArgumentException("Der Kamerahost ist ungültig.", nameof(settings));
        if (connection.RtspUrl is not null && (!Uri.TryCreate(connection.RtspUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("rtsp" or "rtsps") || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0))
            throw new ArgumentException("Die RTSP-URL ist ungültig oder enthält Zugangsdaten.", nameof(settings));
        if (connection.CredentialTarget is not null && (!connection.CredentialTarget.StartsWith("Versammlungsptzsteuerung/", StringComparison.Ordinal) ||
            connection.CredentialTarget.Any(char.IsControl)))
            throw new ArgumentException("Der Verweis auf die Windows-Zugangsdaten ist ungültig.", nameof(settings));
        if (settings.Presets.Count != 4 || settings.Presets.Any(preset => preset is null) ||
            !settings.Presets.Select(preset => preset.Id).SequenceEqual(PresetIds.Ordered))
            throw new ArgumentException("Die vier Positions-IDs und ihre Reihenfolge müssen erhalten bleiben.", nameof(settings));
        foreach (PresetSettings preset in settings.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Any(char.IsControl))
                throw new ArgumentException("Ein Positionsname ist ungültig.", nameof(settings));
            if (preset.Id == PresetIds.Home && (preset.Name != "Home" || preset.Target is not null))
                throw new ArgumentException("Home hat einen festen Namen und keinen lokalen Zielwert.", nameof(settings));
            if (preset.Target is { Zoom: < 0 })
                throw new ArgumentException("Zoom muss zwischen 0 und 32767 liegen.", nameof(settings));
            if (preset.ImageFileName is not null && !IsOwnImageName(preset.ImageFileName))
                throw new ArgumentException("Ein Bildverweis ist kein eigener relativer PNG-Dateiname.", nameof(settings));
        }
        ValidateWindow(settings.Windows.Main);
        ValidateWindow(settings.Windows.Preview);
    }

    private static void ValidateWindow(WindowPlacement? placement)
    {
        if (placement is null) return;
        if (!double.IsFinite(placement.X) || !double.IsFinite(placement.Y) || !double.IsFinite(placement.Width) ||
            !double.IsFinite(placement.Height) || !double.IsFinite(placement.Scale) ||
            placement.Width <= 0 || placement.Height <= 0 || placement.Scale <= 0)
            throw new ArgumentException("Fenstergröße und Skalierung müssen gültige positive Werte haben.");
    }
}
