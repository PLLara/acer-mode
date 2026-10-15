using System.Diagnostics;
using System.Windows.Threading;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerMode.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly AppState state;
    readonly DispatcherTimer disarm;
    bool startWithWindows;

    readonly DispatcherTimer capDebounce;

    public SettingsViewModel(AppState state)
    {
        this.state = state;
        disarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        disarm.Tick += (_, _) => { disarm.Stop(); RestoreArmed = false; };
        capDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        capDebounce.Tick += async (_, _) => { capDebounce.Stop(); await state.EnforceCpuCapAsync(); };
        state.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppState.CpuCapAppliedMhz)) OnPropertyChanged(nameof(CpuCapStatus)); };
        state.ProfilesChanged += () =>
        {
            OnPropertyChanged(nameof(CpuCapEnabled));
            OnPropertyChanged(nameof(CpuCapMhz));
            OnPropertyChanged(nameof(CpuCapStatus));
        };
        if (!state.IsDemo)
        {
            try { startWithWindows = Autostart.IsOn(); } catch (Exception ex) { state.Log.Write("autostart: " + ex.Message); }
        }
        RefreshLog();
    }

    public bool CanAutostart => !state.IsDemo;
    public string AutostartHint => state.IsDemo ? "Indisponível no modo demonstração." : "Abre escondido na bandeja ao entrar no Windows.";
    public string Version => "Acer Mode " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "2");

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (value == startWithWindows || state.IsDemo) return;
            try
            {
                Autostart.Set(value, Environment.ProcessPath!);
                startWithWindows = value;
                state.Log.Write("iniciar com o Windows: " + (value ? "ligado" : "desligado"));
            }
            catch (Exception ex) { state.Banner = "Não foi possível mudar o início com o Windows: " + ex.Message; }
            OnPropertyChanged();
        }
    }

    public bool ShowOsd
    {
        get => state.Settings.ShowOsd;
        set { state.Settings.ShowOsd = value; state.SaveSoon(); OnPropertyChanged(); }
    }

    // ---- teto de frequência da CPU (antigo CpuFreqCapWatchdog) ----
    public bool CpuCapEnabled
    {
        get => state.Settings.CpuCap.Enabled;
        set { UpdateCap(state.Settings.CpuCap with { Enabled = value }); OnPropertyChanged(); }
    }

    public double CpuCapMhz
    {
        get => state.Settings.CpuCap.Mhz;
        set { UpdateCap(state.Settings.CpuCap with { Mhz = (int)value }); OnPropertyChanged(); }
    }

    public int CpuCapMin => CpuCapSettings.MinMhz;
    public int CpuCapMax => CpuCapSettings.MaxMhz;

    public string CpuCapStatus => state.CpuCapAppliedMhz switch
    {
        null => "Não foi possível ler o plano de energia.",
        0 => "Sem limite no plano de energia agora.",
        int mhz => $"Aplicado agora: {mhz:N0} MHz, na tomada. Reaplicado a cada 5 min se for removido.",
    };

    void UpdateCap(CpuCapSettings next)
    {
        state.Settings.CpuCap = next.Normalized();
        state.SaveSoon();
        capDebounce.Stop();
        capDebounce.Start();
        OnPropertyChanged(nameof(CpuCapStatus));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreLabel))]
    bool restoreArmed;

    public string RestoreLabel => RestoreArmed ? "Confirmar restauração" : "Restaurar perfis padrão";

    [ObservableProperty] IReadOnlyList<string> logLines = Array.Empty<string>();

    // Dois cliques, sem diálogo modal: o primeiro arma por 4 s, o segundo restaura.
    [RelayCommand]
    async Task Restore()
    {
        if (!RestoreArmed) { RestoreArmed = true; disarm.Start(); return; }
        disarm.Stop();
        RestoreArmed = false;
        await state.RestoreDefaultsAsync();
        OnPropertyChanged(nameof(ShowOsd));
        state.Log.Write("perfis restaurados para o padrão");
        RefreshLog();
    }

    [RelayCommand]
    void RefreshLog() => LogLines = state.Log.Tail(200).Reverse().ToArray();

    [RelayCommand]
    void OpenFolder() => Process.Start(new ProcessStartInfo("explorer.exe", '"' + state.DataDirectory + '"') { UseShellExecute = true });
}
