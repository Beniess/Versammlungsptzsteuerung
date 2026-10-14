using CameraControl.AppCore;
using CameraControl.AppCore.Control;
using CameraControl.AppCore.Presets;
using CameraControl.AppCore.Storage;
using Microsoft.UI.Dispatching;

namespace CameraControl_WinUI.ViewModels;

public sealed class ShellViewModel : IDisposable
{
    private readonly DispatcherQueue dispatcher;
    private bool disposed;
    public event EventHandler? Changed;
    public ApplicationServices Services { get; }
    public string? LastOrderId { get; private set; }
    public string Message { get; private set; } = "";
    public bool MessageIsError { get; private set; }
    public bool CanControl => Services.CanOperate && !Services.IsSavingSelectedPosition && !Services.IsSavingSelectedPhoto && Services.Camera.IsConnected && !Services.Camera.IsBusy &&
        (!Services.IsInitialized || !Services.Presets.IsBusy);
    public string ControlStatus => Services.Camera.LastError ?? (Services.IsReadOnly ? "Version wird vorbereitet" :
        Services.Camera.IsBusy ? "Auftrag läuft" : Services.Camera.IsConnected ? "Verbunden" : "Nicht verbunden");
    public string Status => "Steuerung: " + ControlStatus + " · Vorschau: " + Services.VideoStatus;
    public string? SelectedPresetId => Services.SelectedPresetId;
    public string SelectedPresetName => Services.SelectedPresetName;
    public bool CanSelectPreset => Services.CanSelectPreset;
    public bool CanSaveSelectedPosition => Services.CanSaveSelectedPosition;
    public string? CanSaveSelectedPositionReason => Services.CanSaveSelectedPositionReason;
    public bool CanTakeSelectedPhoto => Services.CanTakeSelectedPhoto;
    public string? CanTakeSelectedPhotoReason => Services.CanTakeSelectedPhotoReason;

    public Task SaveSelectedPhotoAsync(Func<PhotoConfirmation, CancellationToken, Task<bool>> confirm,
        CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        string name = SelectedPresetName;
        bool saved = await Services.SaveSelectedPhotoAsync(confirm, cancellationToken);
        ShowMessage(saved ? $"Foto für „{name}“ gespeichert." : "Fotoaufnahme abgebrochen.");
    });

    public void SelectPreset(string id)
    {
        try { Services.SelectPreset(id); }
        catch (Exception error) when (UserOperation.IsHandled(error)) { ShowMessage(error.Message, true); }
    }

    public Task RecallSelectedAsync()
    {
        var preset = Services.Settings.Presets.FirstOrDefault(value => value.Id == SelectedPresetId);
        return preset is null ? RunAsync(() => throw new InvalidOperationException("Wähle zuerst eine Position.")) : RecallAsync(preset);
    }

    public Task SaveSelectedPositionAsync() => RunAsync(async () =>
    {
        if (!CanSaveSelectedPosition) throw new InvalidOperationException(CanSaveSelectedPositionReason);
        await Services.SaveSelectedPositionAsync(Services.LifetimeToken);
        ShowMessage("Position gespeichert.");
    });

    public ShellViewModel(ApplicationServices services, DispatcherQueue dispatcher)
    {
        Services = services;
        this.dispatcher = dispatcher;
        Services.Changed += OnServicesChanged;
    }

    public bool CanRecall(PresetSettings preset) => CanSelectPreset && Services.Camera.IsConnected && preset.IsConfigured &&
        Services.Settings.Presets.Any(value => value == preset);

    public string PresetStatus(PresetSettings preset) => !preset.IsConfigured ? "Nicht eingerichtet" :
        LastOrderId == preset.Id ? "Letzter Auftrag" : Services.Camera.IsConnected ? "Bereit" : "Nicht verbunden";

    public async Task RecallAsync(PresetSettings preset)
    {
        await RunAsync(async () =>
        {
            if (!CanRecall(preset)) throw new InvalidOperationException("Diese Position ist noch nicht bereit.");
            if (await Services.RecallAsync(preset, Services.LifetimeToken)) LastOrderId = preset.Id;
        });
    }

    public async Task StopAsync()
    {
        if (!Services.Camera.IsConnected || !Services.Camera.IsMoving) return;
        await RunAsync(() => Services.StopAsync());
    }

    public async Task RunAsync(Func<Task> action)
    {
        Message = "";
        MessageIsError = false;
        RaiseChanged();
        var error = await UserOperation.RunAsync(action);
        if (error is not null) { Message = error; MessageIsError = true; }
        RaiseChanged();
    }

    public void ShowMessage(string message, bool isError = false) { Message = message; MessageIsError = isError; RaiseChanged(); }
    private void OnServicesChanged(object? sender, EventArgs args) =>
        dispatcher.TryEnqueue(() => { if (!disposed) RaiseChanged(); });
    private void RaiseChanged() { if (!disposed) Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { disposed = true; Services.Changed -= OnServicesChanged; }
}
