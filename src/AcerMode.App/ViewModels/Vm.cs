namespace AcerMode.App.ViewModels;

// Os ViewModels são únicos e compartilhados pelos dois shells (janela e flyout).
public sealed class Vm
{
    public Vm(AppState state)
    {
        State = state;
        Modes = new ModesViewModel(state);
        Lighting = new LightingViewModel(state);
    }

    public AppState State { get; }
    public ModesViewModel Modes { get; }
    public LightingViewModel Lighting { get; }
}
