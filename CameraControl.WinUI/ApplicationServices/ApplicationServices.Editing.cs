using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using CameraControl.AppCore;
using CameraControl.AppCore.Control;
using CameraControl.AppCore.Storage;

namespace CameraControl_WinUI;

public sealed partial class ApplicationServices
{
    private IRtspCredentialStore? credentialStore;
    private bool connectionChanging;
    private long connectionRevision;
    public bool IsConnectionChanging { get { lock (stateLock) return connectionChanging; } }
    public bool HasCredentialStore => credentialStore is not null;
    public long ConnectionRevision { get { lock (stateLock) return connectionRevision; } }
    public string? ConnectionWarning { get; private set; }
    public string RtspCredentialTarget => IsQa
        ? "Versammlungsptzsteuerung/QA/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(DataDirectory).ToUpperInvariant()))).ToLowerInvariant() + "/RTSP"
        : "Versammlungsptzsteuerung/RTSP";

    public void ConfigureCredentialStore(IRtspCredentialStore store)
    {
        credentialStore = store ?? throw new ArgumentNullException(nameof(store));
        Notify();
    }

    public Task<RtspCredential?> ReadRtspCredentialAsync(CancellationToken cancellationToken = default) =>
        credentialStore is null || Settings.Connection.CredentialTarget is null ? Task.FromResult<RtspCredential?>(null) :
            credentialStore.ReadAsync(Settings.Connection.CredentialTarget, cancellationToken);

    public Task CheckConnectionAsync(ConnectionSettings connection, CancellationToken cancellationToken = default)
    {
        EnsureQaEndpoint(connection.Host);
        return OwnCameraTask(async token =>
        {
            await using var probe = new CameraSession();
            await probe.ConnectAsync(connection, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task CapturePhotoAsync(CancellationToken cancellationToken = default) =>
        OwnDraftTask(token => Presets.CapturePhotoAsync(token), cancellationToken);

    public Task TakePositionAsync(CancellationToken cancellationToken = default) =>
        OwnDraftTask(token => Presets.TakePositionAsync(token), cancellationToken);

    public async Task SaveConnectionWithCredentialsAsync(ConnectionSettings connection, string? userName,
        string? password, bool removeCredentials, CancellationToken cancellationToken = default)
    {
        EnsureCanOperate();
        lock (stateLock)
            if (Presets.IsBusy || Camera.IsBusy || Camera.IsMoving || foregroundTasks.Any(task => !task.IsCompleted))
                throw new InvalidOperationException("Während eines Kamera- oder Fotoauftrags können Verbindungsdaten nicht geändert werden.");
        await settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        RtspCredential? previousCredential = null;
        var changedCredential = false;
        var committed = false;
        try
        {
            lock (stateLock)
            {
                EnsureCanOperate();
                if (Presets.IsBusy || Camera.IsBusy || Camera.IsMoving || foregroundTasks.Any(task => !task.IsCompleted))
                    throw new InvalidOperationException("Der laufende Kamera- oder Fotoauftrag muss zuerst beendet werden.");
                connectionChanging = true;
                ConnectionWarning = null;
            }
            Notify();
            var loaded = await Repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing))
                throw new SettingsStorageException("Die Einstellungsdatei benötigt eine neue Einrichtung.");
            var baseline = loaded.Status == SettingsLoadStatus.Loaded ? loaded.Settings : Settings;
            var credentialRequested = !string.IsNullOrWhiteSpace(userName) || !string.IsNullOrEmpty(password);
            if (removeCredentials) connection = connection with { CredentialTarget = null };
            else if (credentialRequested)
            {
                var store = credentialStore ?? throw new InvalidOperationException("Die Windows-Zugangsdatenablage ist noch nicht bereit.");
                previousCredential = await store.ReadAsync(RtspCredentialTarget, cancellationToken).ConfigureAwait(false);
                var preserved = baseline.Connection.CredentialTarget == RtspCredentialTarget ? previousCredential : null;
                var user = string.IsNullOrWhiteSpace(userName) ? preserved?.UserName : userName.Trim();
                var secret = string.IsNullOrEmpty(password) ? preserved?.Password : password;
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(secret))
                    throw new ArgumentException("Für neue Zugangsdaten sind Benutzername und Passwort erforderlich.");
                await store.SaveAsync(RtspCredentialTarget, user, secret, cancellationToken).ConfigureAwait(false);
                changedCredential = true;
                connection = connection with { CredentialTarget = RtspCredentialTarget };
            }
            var saved = await Repository.SaveAsync(baseline with { Connection = connection }, cancellationToken: cancellationToken).ConfigureAwait(false);
            committed = true;
            var hostChanged = !string.Equals(baseline.Connection.Host, connection.Host, StringComparison.OrdinalIgnoreCase) ||
                baseline.Connection.ViscaPort != connection.ViscaPort;
            if (hostChanged && Camera.IsConnected)
            {
                try { await Camera.DisconnectAsync().ConfigureAwait(false); }
                catch (CameraOperationException) { ConnectionWarning = "Die alte Kameraverbindung konnte nicht bestätigt beendet werden."; }
            }
            lock (stateLock) { settings = saved; LoadStatus = SettingsLoadStatus.Loaded; LoadMessage = null; connectionRevision++; }
            if (removeCredentials && baseline.Connection.CredentialTarget == RtspCredentialTarget && credentialStore is not null)
            {
                try { await credentialStore.DeleteAsync(RtspCredentialTarget).ConfigureAwait(false); }
                catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
                { ConnectionWarning = "Die Zugangsdatenreferenz wurde entfernt. Die Windows-Ablage konnte nicht bereinigt werden."; }
            }
        }
        catch
        {
            if (!committed && changedCredential && credentialStore is not null)
            {
                if (previousCredential is null) await credentialStore.DeleteAsync(RtspCredentialTarget).ConfigureAwait(false);
                else await credentialStore.SaveAsync(RtspCredentialTarget, previousCredential.UserName, previousCredential.Password).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            lock (stateLock) connectionChanging = false;
            settingsLock.Release();
            Notify();
        }
    }
}
