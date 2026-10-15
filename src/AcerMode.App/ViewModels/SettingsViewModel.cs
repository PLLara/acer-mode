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

    public SettingsViewModel(AppState state)
    {
        this.state = state;
        disarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        disarm.Tick += (_, _) => { disarm.Stop(); RestoreArmed = false; };
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
