namespace AcerMode.App.ViewModels;

// Os ViewModels são únicos e compartilhados pelos dois shells (janela e flyout).
public sealed class Vm
{
    public Vm(AppState state)
    {
        State = state;
        Modes = new ModesViewModel(state);
        Lighting = new LightingViewModel(state);
        System = new SystemViewModel(state);
        Fans = new FansViewModel(state);
        Settings = new SettingsViewModel(state);
        Overclock = new OverclockViewModel(state);
    }

    public OverclockViewModel Overclock { get; }

    public AppState State { get; }
    public ModesViewModel Modes { get; }
    public LightingViewModel Lighting { get; }
    public SystemViewModel System { get; }
    public FansViewModel Fans { get; }
    public SettingsViewModel Settings { get; }
}
