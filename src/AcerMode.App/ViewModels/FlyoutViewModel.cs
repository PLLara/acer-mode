using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerMode.App.ViewModels;

public sealed partial class SwatchVm : ObservableObject
{
    public SwatchVm(Color c)
    {
        Color = c;
        Brush = new SolidColorBrush(c);
        Brush.Freeze();
        Hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public Color Color { get; }
    public SolidColorBrush Brush { get; }
    public string Hex { get; }
    [ObservableProperty] bool isSelected;
}

// Painel da bandeja (handoff de 2026-10-21): modos, iluminação do modo ativo e telemetria.
public sealed partial class FlyoutViewModel : ObservableObject
{
    const int SeriesLength = 60;
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);
    static readonly KbEffect[] Main = { KbEffect.Static, KbEffect.Breathing, KbEffect.Neon };

    readonly DispatcherTimer staleTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public FlyoutViewModel(Vm vm)
    {
        State = vm.State;
        Modes = vm.Modes;
        Lighting = vm.Lighting;
        MainEffects = Lighting.Effects.Where(e => Main.Contains(e.Effect)).ToArray();
        MoreEffects = Lighting.Effects.Where(e => !Main.Contains(e.Effect)).ToArray();
        Swatches = Lighting.Swatches.Select(c => new SwatchVm(c)).ToArray();

        State.PropertyChanged += OnState;
        Lighting.PropertyChanged += OnLighting;
        staleTimer.Tick += (_, _) => RefreshTelemetry();
        SyncSwatches();
        RefreshTelemetry();
    }

    public AppState State { get; }
    public ModesViewModel Modes { get; }
    public LightingViewModel Lighting { get; }
    public IReadOnlyList<EffectOption> MainEffects { get; }
    public IReadOnlyList<EffectOption> MoreEffects { get; }
    public IReadOnlyList<SwatchVm> Swatches { get; }

    // ---- cabeçalho ----
    public bool Querying => State.ActiveMode is null;
    public string ActiveName => State.ActiveMode?.ToString() ?? "";

    // ---- efeito escolhido em "Mais" aparece no lugar do rótulo ----
    public bool MoreSelected => !Main.Contains(Lighting.Effect);
    public string MoreLabel => MoreSelected ? KbEffects.Label(Lighting.Effect) : "Mais";

    [RelayCommand] void PickMore(EffectOption o) => Lighting.Effect = o.Effect;
    [RelayCommand] void PickSwatch(SwatchVm s) => Lighting.PickSwatchCommand.Execute(s.Color);

    // ---- telemetria ----
    [ObservableProperty] double? cpu;
    [ObservableProperty] double? gpu;
    [ObservableProperty] double? fan;
    [ObservableProperty] bool stale;
    [ObservableProperty] IReadOnlyList<double> cpuSeries = Array.Empty<double>();
    [ObservableProperty] IReadOnlyList<double> gpuSeries = Array.Empty<double>();
    [ObservableProperty] IReadOnlyList<double> fanSeries = Array.Empty<double>();

    public void SetVisible(bool visible)
    {
        if (visible) { RefreshTelemetry(); staleTimer.Start(); } else staleTimer.Stop();
    }

    void OnState(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Latest)) RefreshTelemetry();
        if (e.PropertyName == nameof(AppState.ActiveMode)) { OnPropertyChanged(nameof(Querying)); OnPropertyChanged(nameof(ActiveName)); }
    }

    void OnLighting(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LightingViewModel.Color)) SyncSwatches();
        if (e.PropertyName == nameof(LightingViewModel.Effect)) { OnPropertyChanged(nameof(MoreSelected)); OnPropertyChanged(nameof(MoreLabel)); }
    }

    void SyncSwatches()
    {
        foreach (var sw in Swatches) sw.IsSelected = sw.Color == Lighting.Color;
    }

    // Sem leitura válida mostra "—"; nunca zero inventado. A ventoinha do cartão é a da CPU.
    void RefreshTelemetry()
    {
        var last = State.Latest;
        Stale = last is not null && DateTime.Now - last.At > StaleAfter;
        Cpu = last?.CpuTempC;
        Gpu = last?.GpuTempC;
        Fan = last?.CpuFanRpm;
        var h = State.Sensors.History.Snapshot();
        var tail = h.Skip(Math.Max(0, h.Count - SeriesLength)).ToArray();
        CpuSeries = tail.Where(r => r.CpuTempC is not null).Select(r => (double)r.CpuTempC!).ToArray();
        GpuSeries = tail.Where(r => r.GpuTempC is not null).Select(r => (double)r.GpuTempC!).ToArray();
        FanSeries = tail.Where(r => r.CpuFanRpm is not null).Select(r => (double)r.CpuFanRpm!).ToArray();
    }
}
