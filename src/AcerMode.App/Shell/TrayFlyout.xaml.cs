using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;

namespace AcerMode.App.Shell;

public sealed class FlyoutContext(Vm vm)
{
    public AppState State => vm.State;
    public ModesViewModel Modes => vm.Modes;
    public LightingViewModel Lighting => vm.Lighting;
    public IReadOnlyList<EffectOption> QuickEffects { get; } = vm.Lighting.Effects.Take(3).ToArray();
}

// Acesso rápido acima da bandeja: modos, iluminação do modo ativo e temperaturas.
public partial class TrayFlyout : Window
{
    readonly Vm vm;
    bool hiding;
    DateTime hiddenAt;

    public TrayFlyout(Vm vm, Action<string> openWindowAt)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = new FlyoutContext(vm);
        Deactivated += (_, _) => HideAnimated();
        OpenButton.Click += (_, _) => { HideAnimated(); openWindowAt("modos"); };
        OpenMain.Click += (_, _) => { HideAnimated(); openWindowAt("modos"); };
        MoreEffects.Click += (_, _) => { HideAnimated(); openWindowAt("iluminacao"); };
        vm.Lighting.SweepRequested += Keyboard.Sweep;
        IsVisibleChanged += (_, _) =>
        {
            App.FlyoutVisible = IsVisible;
            vm.State.Sensors.UiVisible = IsVisible || App.ShellVisible;
        };
    }

    public FrameworkElement RootElement => Root;

    // O clique no ícone que fecha o flyout (perda de foco) não deve reabri-lo logo em seguida.
    public void Toggle()
    {
        if (IsVisible) { HideAnimated(); return; }
        if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
        ShowAnimated();
    }

    public void ShowAnimated()
    {
        if (vm.State.ActiveMode is { } m && !vm.State.Settings.IndependentLighting) vm.Lighting.EditingMode = m;
        var area = SystemParameters.WorkArea;
        // a margem de 12 px do Root já faz o respiro até a borda da área de trabalho
        Left = area.Right - Width;
        Top = area.Bottom - Height;
        hiding = false;
        Show();
        Activate();
        Root.BeginAnimation(OpacityProperty, Motion.FromTo(0, 1, 200));
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.FromTo(0.96, 1, 260, Motion.Spring));
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.FromTo(0.96, 1, 260, Motion.Spring));
        Shift.BeginAnimation(TranslateTransform.YProperty, Motion.FromTo(14, 0, 220));
    }

    public void HideAnimated()
    {
        if (!IsVisible || hiding) return;
        hiding = true;
        hiddenAt = DateTime.Now;
        var fade = Motion.To(0, 140);
        fade.Completed += (_, _) => { if (hiding) { Hide(); hiding = false; } };
        Root.BeginAnimation(OpacityProperty, fade);
        Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(10, 140));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) HideAnimated();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.ReallyExiting) { e.Cancel = true; HideAnimated(); }
        base.OnClosing(e);
    }
}
