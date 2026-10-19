using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerMode.App.ViewModels;

// Overclock da GPU: os sliders só valem ao tocar em Aplicar (nunca ao arrastar).
public sealed partial class OverclockViewModel : ObservableObject
{
    readonly AppState state;

    public OverclockViewModel(AppState state)
    {
        this.state = state;
        var s = state.Settings.GpuOc;
        coreMhz = s.CoreMhz;
        memMhz = s.MemMhz;
        Refresh();
    }

    public int MaxCore => GpuOcSettings.MaxCoreMhz;
    public int MaxMem => GpuOcSettings.MaxMemMhz;
    public int CoreStep => GpuOcSettings.CoreStep;
    public int MemStep => GpuOcSettings.MemStep;

    [ObservableProperty] double coreMhz;
    [ObservableProperty] double memMhz;
    [ObservableProperty] string driverText = "";
    [ObservableProperty] string? externalWarning;
    [ObservableProperty] string? guardStatus;

    public bool Active => state.Overclock.Active;
    public string StateText => Active
        ? $"Ligado: núcleo +{state.Overclock.Applied.CoreMhz} MHz, memória +{state.Overclock.Applied.MemMhz} MHz"
        : "Desligado";
    public bool HasPendingChange => !Active
        ? CoreMhz > 0 || MemMhz > 0
        : (int)CoreMhz != state.Overclock.Applied.CoreMhz || (int)MemMhz != state.Overclock.Applied.MemMhz;

    partial void OnCoreMhzChanged(double value) => OnPropertyChanged(nameof(HasPendingChange));
    partial void OnMemMhzChanged(double value) => OnPropertyChanged(nameof(HasPendingChange));

    [RelayCommand]
    void Apply()
    {
        var s = new GpuOcSettings(true, (int)Math.Round(CoreMhz), (int)Math.Round(MemMhz)).Normalized();
        try
        {
            state.Overclock.Apply(s);
            state.Settings.GpuOc = s with { Enabled = state.Overclock.Active };
            GuardStatus = null;
        }
        catch (HardwareException ex)
        {
            state.Settings.GpuOc = s with { Enabled = false };
            state.Banner = ex.Message;
        }
        state.SaveSettings();
        Refresh();
    }

    [RelayCommand]
    void TurnOff()
    {
        try { state.Overclock.Apply(state.Settings.GpuOc with { Enabled = false }); }
        catch (HardwareException ex) { state.Banner = ex.Message; }
        state.Settings.GpuOc = state.Settings.GpuOc with { Enabled = false };
        state.SaveSettings();
        Refresh();
    }

    [RelayCommand]
    void ZeroExternal()
    {
        state.Overclock.ZeroExternal();
        Refresh();
    }

    // A proteção reverteu (calor, driver, travamento): o OC só volta se a pessoa aplicar de novo.
    public void ReportGuard(string message)
    {
        state.Settings.GpuOc = state.Settings.GpuOc with { Enabled = false };
        state.SaveSettings();
        GuardStatus = message;
        Refresh();
    }

    public void Refresh()
    {
        var d = state.Overclock.ReadDriver();
        DriverText = d is { } v ? $"Driver agora: núcleo {Signed(v.CoreMhz)} MHz, memória {Signed(v.MemMhz)} MHz" : "Driver da NVIDIA indisponível.";
        ExternalWarning = state.Overclock.ExternalOffsets() is { } e
            ? $"Overclock de outro programa ativo (núcleo {Signed(e.CoreMhz)}, memória {Signed(e.MemMhz)} MHz). O NVIDIA App ou o PredatorSense pode ter ligado."
            : null;
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasPendingChange));
    }

    static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
}
