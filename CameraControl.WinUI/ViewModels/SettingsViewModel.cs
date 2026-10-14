using CameraControl.AppCore;

namespace CameraControl_WinUI.ViewModels;

public sealed class SettingsViewModel
{
    private readonly ShellViewModel shell;
    private ConnectionSettings? displayedConnection;
    private bool initialized;
    private readonly Dictionary<string, string> localNames = [];
    private bool credentialEdited;
    public event EventHandler? Changed;
    public ApplicationServices Services => shell.Services;
    public bool IsBusy { get; private set; }
    public bool IsSavingPreset { get; private set; }
    public bool ConnectionDirty { get; private set; }
    public string Host { get; private set; } = "";
    public double Port { get; private set; } = 5678;
    public string Rtsp { get; private set; } = "";
    public string UserName { get; private set; } = "";
    private string password = "";
    public bool RemoveCredentials { get; private set; }
    public string SelectedId => shell.SelectedPresetId ?? PresetIds.Leser;
    public string Name { get; private set; } = "Leser";
    public bool NameDirty => Services.IsInitialized && Name != Services.Settings.Presets.Single(value => value.Id == SelectedId).Name;
    public long ImageRevision { get; private set; }
    public bool IsHome => SelectedId == PresetIds.Home;
    public bool CanEdit => Services.CanOperate && !IsBusy && !Services.Presets.IsBusy && !Services.IsSavingSelectedPosition && !Services.IsSavingSelectedPhoto;
    public string TargetText => Services.Presets.CurrentDraft?.Settings.Target is { } target
        ? $"Ziel: Pan {target.Pan} · Tilt {target.Tilt} · Zoom {target.Zoom}"
        : IsHome ? "Home verwendet den nativen Kamerabefehl." : "Ziel noch nicht eingerichtet.";
    public string PhotoHelp => IsHome ? "Foto aufnehmen übernimmt nur das Bild in den Entwurf."
        : "Foto aufnehmen übernimmt das Bild und die aktuellen Pan-, Tilt- und Zoomwerte in den Entwurf.";

    public SettingsViewModel(ShellViewModel shell) => this.shell = shell;

    public void Refresh()
    {
        if (!Services.IsInitialized) { Changed?.Invoke(this, EventArgs.Empty); return; }
        if (!initialized || !ConnectionDirty && displayedConnection != Services.Settings.Connection)
        {
            displayedConnection = Services.Settings.Connection;
            Host = displayedConnection.Host ?? "";
            Port = displayedConnection.ViscaPort;
            Rtsp = displayedConnection.RtspUrl ?? "";
        }
        var savedName = Services.Settings.Presets.Single(value => value.Id == SelectedId).Name;
        Name = localNames.TryGetValue(SelectedId, out var localName) ? localName : savedName;
        initialized = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetConnection(string host, double port, string rtsp)
    {
        Host = host; Port = port; Rtsp = rtsp; ConnectionDirty = true;
    }

    public void SetCredential(string user, string secret, bool remove)
    {
        UserName = user; password = secret; RemoveCredentials = remove; credentialEdited = true;
    }

    public void SetName(string name)
    {
        Name = name;
        if (!Services.IsInitialized || IsHome) return;
        localNames[SelectedId] = name;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Select(string id)
    {
        if (!shell.CanSelectPreset || IsBusy) return;
        shell.SelectPreset(id);
        ImageRevision++;
        Refresh();
    }

    public async Task LoadCredentialUserAsync()
    {
        if (credentialEdited || !Services.HasCredentialStore || !Services.IsInitialized) return;
        await shell.RunAsync(async () =>
        {
            var credential = await Services.ReadRtspCredentialAsync();
            if (!credentialEdited) UserName = credential?.UserName ?? "";
        });
        Refresh();
    }

    public Task CheckConnectionAsync() => RunAsync(async () =>
    {
        await Services.CheckConnectionAsync(BuildConnection(), Services.LifetimeToken);
        shell.ShowMessage("Verbindung geprüft. Speichern und verbinden übernimmt die Daten.");
    });

    public Task SaveConnectionAsync() => RunAsync(async () =>
    {
        await Services.SaveConnectionWithCredentialsAsync(BuildConnection(), credentialEdited ? UserName : null,
            credentialEdited ? password : null, RemoveCredentials, Services.LifetimeToken);
        ConnectionDirty = false;
        credentialEdited = false;
        password = "";
        RemoveCredentials = false;
        shell.ShowMessage(Services.ConnectionWarning ?? "Verbindungsdaten gespeichert. Verbinden übernimmt die neue Steuerungsverbindung.");
    });

    public Task ReconnectAsync() => RunAsync(() => Services.ConnectAsync(Services.Settings.Connection, Services.LifetimeToken));

    public Task SaveAndConnectAsync() => RunAsync(async () =>
    {
        await Services.SaveConnectionWithCredentialsAsync(BuildConnection(), credentialEdited ? UserName : null,
            credentialEdited ? password : null, RemoveCredentials, Services.LifetimeToken);
        ConnectionDirty = false;
        credentialEdited = false;
        password = "";
        RemoveCredentials = false;
        await Services.ConnectAsync(Services.Settings.Connection, Services.LifetimeToken);
        shell.ShowMessage(Services.ConnectionWarning ?? "Verbindungsdaten gespeichert.");
    });
    public Task TakePositionAsync() => RunDraftAsync(() => Services.TakePositionAsync(Services.LifetimeToken));
    public Task CapturePhotoAsync() => RunDraftAsync(() => Services.CapturePhotoAsync(Services.LifetimeToken));

    public async Task SavePresetAsync()
    {
        IsSavingPreset = true;
        try
        {
            await RunDraftAsync(async () =>
            {
                if (IsHome) return;
                var id = SelectedId;
                Services.Presets.Rename(Name);
                await Services.SavePresetAsync(Services.LifetimeToken);
                localNames.Remove(id);
                shell.ShowMessage("Name gespeichert.");
            });
        }
        finally { IsSavingPreset = false; Refresh(); }
    }

    public void CancelPreset()
    {
        try { Services.Presets.Cancel(); localNames.Remove(SelectedId); ImageRevision++; Refresh(); }
        catch (InvalidOperationException error) { shell.ShowMessage(error.Message, true); }
    }

    public Task ResetAsync() => RunDraftAsync(() => Services.ResetSettingsAsync(Services.LifetimeToken));

    private async Task RunDraftAsync(Func<Task> action)
    {
        await RunAsync(action);
        ImageRevision++;
        Refresh();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        Refresh();
        try { await shell.RunAsync(action); }
        finally { IsBusy = false; Refresh(); }
    }

    private ConnectionSettings BuildConnection()
    {
        if (!double.IsFinite(Port) || Port != Math.Truncate(Port) || Port is < 1 or > 65535)
            throw new ArgumentException("Der VISCA-Port muss eine ganze Zahl zwischen 1 und 65535 sein.");
        return new ConnectionSettings(string.IsNullOrWhiteSpace(Host) ? null : Host.Trim(), (int)Port,
            string.IsNullOrWhiteSpace(Rtsp) ? null : Rtsp.Trim(), Services.Settings.Connection.CredentialTarget);
    }
}
