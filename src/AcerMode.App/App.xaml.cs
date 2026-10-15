using System.IO;
using System.Windows;
using AcerMode.App.Shell;
using AcerMode.App.Startup;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;
using Microsoft.Win32;

namespace AcerMode.App;

public partial class App : Application
{
    public static bool ReallyExiting { get; private set; }
    public static bool FlyoutVisible { get; set; }
    public static bool ShellVisible { get; set; }

    SingleInstance? instance;
    IAcerHardware? hardware;
    HardwareKey? key;
    Composition? comp;
    ShellWindow? shell;
    TrayFlyout? flyout;
    System.Windows.Threading.DispatcherTimer? cpuCapTimer;
    OsdWindow? osd;
    Tray? tray;

    public App()
    {
        // Falha inesperada vai para crash.log; nunca some em silêncio.
        DispatcherUnhandledException += (_, e) => { WriteCrash(e.Exception); e.Handled = !ReallyExiting && e.Exception is not OutOfMemoryException; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrash(e.Exception); e.SetObserved(); };
    }

    static void WriteCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.AppendAllText(Path.Combine(SettingsStore.DefaultDirectory, "crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n\r\n");
        }
        catch (IOException) { }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AccentManager.Install(Resources);
        var args = e.Args;

        // Comandos sem janela sempre terminam o processo, mesmo quando falham.
        int? cliCode;
        try { cliCode = await Cli.TryRunAsync(args); }
        catch (Exception ex) { WriteCrash(ex); Shutdown(1); return; }
        if (cliCode is { } code) { Shutdown(code); return; }

        bool demo = args.Contains("--demo");
        if (!demo && !Elevation.IsAdmin)
        {
            Elevation.Relaunch(args);
            Shutdown();
            return;
        }

        instance = new SingleInstance(demo ? "AcerModeDemo" : "AcerModeSingle");
        if (!instance.IsFirst) { instance.SignalFirst(); Shutdown(); return; }

        // Uma falha na partida não pode deixar um processo invisível segurando a instância
        // única (cada abertura seguinte só "sinalizaria" o morto): registra e encerra.
        try { await StartAsync(args, demo); }
        catch (Exception ex)
        {
            WriteCrash(ex);
            ReallyExiting = true;
            tray?.Dispose();
            tray = null;
            instance.Dispose();
            instance = null;
            Shutdown(1);
        }
    }

    async Task StartAsync(string[] args, bool demo)
    {
        hardware = demo ? new FakeHardware() : new AcerWmi();
        comp = await Composition.CreateAsync(hardware, demo);
        var state = comp.State;

        if (!demo)
        {
            try { Autostart.Refresh(Environment.ProcessPath!); }
            catch (Exception ex) { state.Log.Write("autostart: " + ex.Message); }
            key = new HardwareKey(state.Log);
            key.Pressed += () => Dispatcher.InvokeAsync(state.ToggleTurboAsync);
            if (!key.Start()) state.Banner = "Botão físico indisponível.";
        }

        SystemEvents.PowerModeChanged += OnPower;
        var vmRef = comp.Vm;
        state.Sensors.GuardTriggered += async (action, reading) =>
        {
            state.Log.Write("FanGuard: " + action);
            try { await state.Modes.RevertFansToAutoAsync(); }
            catch (HardwareException ex) { state.Log.Write("FanGuard falhou: " + ex.Message); }
            AppState.Ui(() =>
            {
                // Personalizado só volta quando a pessoa escolher de novo: o perfil passa a Auto.
                if (state.ActiveMode is { } m)
                {
                    var p = state.Settings.Profile(m);
                    p.Fans = p.Fans with { Mode = FanMode.Auto };
                    state.SaveSettings();
                    state.NotifyProfilesChanged();
                }
                var msg = FanGuard.Message(action, reading);
                state.Banner = msg;
                vmRef.Fans.ReportGuard(msg);
            });
        };
        state.Sensors.Start();

        // Vigia do teto da CPU: a cada 5 min, como o antigo CpuFreqCapWatchdog.
        cpuCapTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        cpuCapTimer.Tick += async (_, _) => await state.EnforceCpuCapAsync();
        cpuCapTimer.Start();

        shell = new ShellWindow(comp.Vm);
        flyout = new TrayFlyout(comp.Vm, page => { ShowShell(); shell.Navigate(page); });
        osd = new OsdWindow();
        tray = new Tray(state, () => flyout.Toggle(), ShowShell, ExitApp,
            getAutostart: () => { try { return Autostart.IsOn(); } catch (Exception) { return false; } },
            setAutostart: on =>
            {
                try { Autostart.Set(on, Environment.ProcessPath!); }
                catch (Exception ex) { state.Banner = "Não foi possível mudar o início com o Windows: " + ex.Message; }
            });
        state.ModeApplied += m =>
        {
            tray.UpdateIcon(m);
            if (state.Settings.ShowOsd) osd.Flash(m);
        };
        instance!.Listen(() => Dispatcher.BeginInvoke(ShowShell));
        if (!args.Contains("--tray")) ShowShell();
    }

    public void ShowShell() => shell?.ShowAnimated();

    async void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || comp is null) return;
        await Task.Delay(3000);
        try
        {
            await comp.State.EnforceCpuCapAsync();
            var w = await comp.State.Modes.ReapplyActiveAsync();
            comp.State.Log.Write("retomada: perfil reaplicado");
            if (w.Count > 0) AppState.Ui(() => comp.State.Banner = string.Join(" ", w));
        }
        catch (HardwareException ex) { comp.State.Log.Write("retomada: " + ex.Message); }
    }

    public async void ExitApp()
    {
        ReallyExiting = true;
        if (comp is { } c && c.State.Modes.ActiveFans.Mode == FanMode.Custom)
        {
            try { await c.State.Modes.RevertFansToAutoAsync().WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (Exception ex) { c.State.Log.Write("saída: ventoinhas não voltaram ao Auto: " + ex.Message); }
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPower;
        tray?.Dispose();
        key?.Dispose();
        comp?.State.Sensors.Dispose();
        (hardware as IDisposable)?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}

// Monta o grafo de objetos (hardware real ou simulado, configurações, serviços, ViewModels).
public sealed class Composition
{
    public required AppState State { get; init; }
    public required Vm Vm { get; init; }

    public static async Task<Composition> CreateAsync(IAcerHardware hw, bool demo)
    {
        var dir = SettingsStore.DefaultDirectory;
        Directory.CreateDirectory(dir);
        string prefix = demo ? "demo-" : "";
        var log = new AppLog(Path.Combine(dir, prefix + "events.log"));
        var store = new SettingsStore(Path.Combine(dir, prefix + "settings.json"));
        var (settings, warning) = store.Load();
        var dirty = new DirtyMarker(Path.Combine(dir, prefix + "fans-dirty"));

        // O ModeService lê sempre as configurações atuais (Restaurar padrões troca o objeto).
        AppState? stateRef = null;
        var modes = new ModeService(hw, () => stateRef?.Settings ?? settings, dirty, log);
        var sensors = new SensorMonitor(hw, () => modes.ActiveFans);
        var cpuCap = new CpuCapService(demo ? new FakePowerConfig() : new PowerCfg(), log);
        var state = stateRef = new AppState(hw, modes, sensors, store, settings, log, demo, dir, cpuCap);

        log.Write(demo ? "iniciado (demonstração)" : "iniciado");
        var warnings = new List<string>();
        if (warning is not null) warnings.Add(warning);
        ThermalMode? active = null;
        try
        {
            await modes.RecoverAfterCrashAsync();
            warnings.AddRange(await modes.ReapplyActiveAsync());
            active = modes.Active;
        }
        catch (HardwareException ex) { warnings.Add(ex.Message); }
        state.SetInitialMode(active, warnings);

        try { var r = await hw.ReadSensorsAsync(); state.Latest = r; state.SensorsAvailable = r.HasTemps; }
        catch (HardwareException) { state.SensorsAvailable = false; }
        await state.EnforceCpuCapAsync();

        return new Composition { State = state, Vm = new Vm(state) };
    }
}
