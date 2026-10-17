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
    readonly LatestWins<(FanSettings Fans, ThermalMode For)> writer;
    readonly DispatcherTimer debounce;
    bool loading, followActive = true;

    public FansViewModel(AppState state)
    {
        this.state = state;
        editingMode = state.ActiveMode ?? ThermalMode.Balanced;
        Options = new[]
        {
            new FanModeOption(FanMode.Smart, "Inteligente"),
            new FanModeOption(FanMode.Auto, "Automático da BIOS"),
            new FanModeOption(FanMode.Max, "Máximo"),
            new FanModeOption(FanMode.Custom, "Personalizado"),
        };

        writer = new LatestWins<(FanSettings Fans, ThermalMode For)>(async x =>
        {
            if (state.Modes.Target != x.For) return;   // o modo mudou (botão físico): o perfil novo já valeu
            var w = await state.Modes.ApplyFansAsync(x.Fans);
            if (w.Count > 0) AppState.Ui(() => state.Banner = string.Join(" ", w));
        });
        writer.Error += ex => AppState.Ui(() => state.Banner = AppState.Friendly(ex));
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            if (IsLive) _ = writer.PostAsync((state.Settings.Profile(EditingMode).Fans, EditingMode));
        };

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
        OnPropertyChanged(nameof(ModeHint));
        if (!loading) Save();
    }

    partial void OnCpuPercentChanged(double value) { if (!loading) Save(); }
    partial void OnGpuPercentChanged(double value) { if (!loading) Save(); }

    // Inteligente e Personalizado dependem de leitura de temperatura.
    void RefreshAvailability()
    {
        foreach (var o in Options) o.Enabled = o.Mode is not (FanMode.Smart or FanMode.Custom) || state.SensorsAvailable;
        OnPropertyChanged(nameof(CustomAvailable));
    }

    public string ModeHint => Mode switch
    {
        FanMode.Smart => $"Até {FanCurve.StartC} °C a BIOS cuida. Acima disso o app assume aos poucos: 40% a 70 °C, 60% a 80 °C, 80% a 88 °C e 100% a 95 °C, subindo no máximo 10% a cada 2 s (a partir de {FanCurve.EmergencyC} °C vai direto). Volta para a BIOS abaixo de {FanCurve.StopC} °C.",
        FanMode.Auto => "A BIOS decide sozinha. No Turbo ela sobe pouco: a CPU pode passar de 95 °C com as ventoinhas a ~3.700 rpm.",
        FanMode.Max => "Ventoinhas no máximo o tempo todo (~7.700 rpm). Mais frio, mais barulho.",
        _ => "Velocidade fixa escolhida por você, protegida pelo limite de 85 °C.",
    };

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
