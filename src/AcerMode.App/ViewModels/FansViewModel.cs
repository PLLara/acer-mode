using System.Windows.Threading;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AcerMode.App.ViewModels;

public sealed partial class FanModeOption(FanMode mode, string label) : ObservableObject
{
    public FanMode Mode { get; } = mode;
    public string Label { get; } = label;
    [ObservableProperty] bool enabled = true;
}

// Ventoinhas por modo: Automático, Máximo ou Personalizado (% fixo, mínimo 30 %).
// Personalizado depende de leitura de temperatura, porque o FanGuard precisa dela.
public sealed partial class FansViewModel : ObservableObject
{
    readonly AppState state;
    readonly LatestWins<FanSettings> writer;
    readonly DispatcherTimer debounce;
    bool loading, followActive = true;

    public FansViewModel(AppState state)
    {
        this.state = state;
        editingMode = state.ActiveMode ?? ThermalMode.Balanced;
        Options = new[]
        {
            new FanModeOption(FanMode.Auto, "Automático"),
            new FanModeOption(FanMode.Max, "Máximo"),
            new FanModeOption(FanMode.Custom, "Personalizado"),
        };

        writer = new LatestWins<FanSettings>(async f =>
        {
            var w = await state.Modes.ApplyFansAsync(f);
            if (w.Count > 0) AppState.Ui(() => state.Banner = string.Join(" ", w));
        });
        writer.Error += ex => AppState.Ui(() => state.Banner = AppState.Friendly(ex));
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) => { debounce.Stop(); if (IsLive) _ = writer.PostAsync(state.Settings.Profile(EditingMode).Fans); };

        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.Latest)) { OnPropertyChanged(nameof(CpuRpm)); OnPropertyChanged(nameof(GpuRpm)); }
            if (e.PropertyName == nameof(AppState.SensorsAvailable)) RefreshAvailability();
            if (e.PropertyName == nameof(AppState.ActiveMode)) OnPropertyChanged(nameof(LiveHint));
        };
        state.ModeApplied += m => { if (followActive) EditingMode = m; };
        state.ProfilesChanged += Load;
        RefreshAvailability();
        Load();
    }

    public IReadOnlyList<ThermalMode> Modes { get; } = ThermalModes.All;
    public IReadOnlyList<FanModeOption> Options { get; }

    [ObservableProperty] ThermalMode editingMode;
    [ObservableProperty] FanMode mode;
    [ObservableProperty] double cpuPercent = 60;
    [ObservableProperty] double gpuPercent = 60;
    [ObservableProperty] string? guardStatus;

    public bool IsCustom => Mode == FanMode.Custom;
    public double? CpuRpm => state.Latest?.CpuFanRpm;
    public double? GpuRpm => state.Latest?.GpuFanRpm;
    public bool CustomAvailable => state.SensorsAvailable;
    public bool IsLive => state.ActiveMode == EditingMode;
    public string LiveHint => IsLive ? "Mudanças valem na hora." : $"Editando {EditingMode}: aplica quando esse modo for ativado.";
    public static int MinPercent => FanSettings.MinPercent;

    public void ReportGuard(string message) => GuardStatus = message;

    partial void OnEditingModeChanged(ThermalMode value)
    {
        followActive = value == state.ActiveMode;
        Load();
    }

    partial void OnModeChanged(FanMode value)
    {
        OnPropertyChanged(nameof(IsCustom));
        if (!loading) Save();
    }

    partial void OnCpuPercentChanged(double value) { if (!loading) Save(); }
    partial void OnGpuPercentChanged(double value) { if (!loading) Save(); }

    void RefreshAvailability()
    {
        Options[2].Enabled = state.SensorsAvailable;
        OnPropertyChanged(nameof(CustomAvailable));
    }

    void Load()
    {
        loading = true;
        var f = state.Settings.Profile(EditingMode).Fans.Normalized();
        Mode = f.Mode;
        CpuPercent = f.CpuPercent;
        GpuPercent = f.GpuPercent;
        loading = false;
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveHint));
    }

    void Save()
    {
        var f = new FanSettings(Mode, (int)Math.Round(CpuPercent), (int)Math.Round(GpuPercent)).Normalized();
        state.Settings.Profile(EditingMode).Fans = f;
        state.SaveSoon();
        if (IsLive) { debounce.Stop(); debounce.Start(); }
    }
}
