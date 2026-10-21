using System.IO;
using System.Windows;
using System.Windows.Threading;
using AcerMode.App.Theme;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AcerMode.App.ViewModels;

// Estado compartilhado pela janela, pelo flyout, pelo OSD e pela bandeja.
public sealed partial class AppState : ObservableObject
{
    readonly SettingsStore store;
    readonly LatestWins<ThermalMode> modeWriter;
    readonly DispatcherTimer saveTimer;

    public AppState(IAcerHardware hardware, ModeService modes, SensorMonitor sensors, SettingsStore store,
                    AppSettings settings, AppLog log, bool isDemo, string dataDirectory, CpuCapService cpuCap,
                    OverclockService overclock)
    {
        Hardware = hardware; Modes = modes; Sensors = sensors; this.store = store; CpuCap = cpuCap; Overclock = overclock;
        Settings = settings; Log = log; IsDemo = isDemo; DataDirectory = dataDirectory;

        modeWriter = new LatestWins<ThermalMode>(ApplyCoreAsync);
        modeWriter.Error += ex => Ui(() => { RequestedMode = null; Banner = Friendly(ex); });

        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettings(); };

        sensors.Updated += r => Ui(() => { Latest = r; SensorsAvailable = r.HasTemps; });
    }

    public IAcerHardware Hardware { get; }
    public ModeService Modes { get; }
    public SensorMonitor Sensors { get; }
    public AppLog Log { get; }
    public bool IsDemo { get; }
    public string DataDirectory { get; }
    public AppSettings Settings { get; private set; }
    public CpuCapService CpuCap { get; }
    public OverclockService Overclock { get; }

    // Valor que o plano de energia tem agora (null = não deu para ler).
    [ObservableProperty] int? cpuCapAppliedMhz;

    // Reaplica o teto se foi removido (substitui o antigo CpuFreqCapWatchdog). powercfg é
    // síncrono e leva ~100 ms, então roda fora da thread da interface.
    public async Task EnforceCpuCapAsync()
    {
        var wanted = Settings.CpuCap;
        try
        {
            int? now = await Task.Run(() => { CpuCap.Enforce(wanted); return CpuCap.CurrentMhz(); });
            CpuCapAppliedMhz = now;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write("teto da CPU: " + ex.Message);
            Banner = "Não foi possível aplicar o teto da CPU: " + ex.Message;
        }
    }

    [ObservableProperty] ThermalMode? activeMode;
    [ObservableProperty] SensorReading? latest;
    [ObservableProperty] bool sensorsAvailable = true;
    [ObservableProperty] string? banner;

    public event Action<ThermalMode>? ModeApplied;
    public event Action? ProfilesChanged;

    // Modo pedido e ainda não confirmado pela BIOS (o cartão mostra "Aplicando…").
    [ObservableProperty] ThermalMode? requestedMode;

    public Task SelectModeAsync(ThermalMode mode)
    {
        if (mode != ActiveMode) RequestedMode = mode;
        return modeWriter.PostAsync(mode);
    }

    async Task ApplyCoreAsync(ThermalMode mode)
    {
        var r = await Modes.ApplyModeAsync(mode);
        OnApplied(r);
    }

    // Botão físico: alterna Turbo e volta ao modo anterior.
    public async Task ToggleTurboAsync()
    {
        try { if (await Modes.ToggleTurboAsync() is { } r) OnApplied(r); }
        catch (HardwareException ex) { Banner = Friendly(ex); }
    }

    public void SetInitialMode(ThermalMode? mode, IReadOnlyList<string> warnings)
    {
        ActiveMode = mode;
        if (mode is { } m) AccentManager.Apply(m, animate: false);
        if (warnings.Count > 0) Banner = string.Join(" ", warnings);
    }

    void OnApplied(ModeChangeResult r)
    {
        ActiveMode = r.Mode;
        if (RequestedMode == r.Mode) RequestedMode = null;
        AccentManager.Apply(r.Mode, animate: true);
        Banner = r.Warnings.Count > 0 ? string.Join(" ", r.Warnings) : null;
        ModeApplied?.Invoke(r.Mode);
    }

    public void SaveSoon() { saveTimer.Stop(); saveTimer.Start(); }

    public void SaveSettings()
    {
        try { store.Save(Settings); }
        catch (IOException ex) { Banner = "Não foi possível salvar as configurações: " + ex.Message; }
        catch (UnauthorizedAccessException ex) { Banner = "Não foi possível salvar as configurações: " + ex.Message; }
    }

    public void NotifyProfilesChanged() => ProfilesChanged?.Invoke();

    public async Task RestoreDefaultsAsync()
    {
        Settings = AppSettings.Defaults();
        SaveSettings();
        NotifyProfilesChanged();
        await EnforceCpuCapAsync();
        try
        {
            var w = await Modes.ReapplyActiveAsync();
            Banner = w.Count > 0 ? string.Join(" ", w) : null;
        }
        catch (HardwareException ex) { Banner = Friendly(ex); }
    }

    public static string Friendly(Exception ex) => ex is HardwareException ? ex.Message : "Algo deu errado: " + ex.Message;

    public static void Ui(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) a(); else d.BeginInvoke(a);
    }
}
