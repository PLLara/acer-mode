using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Shell;

// Painel da bandeja (handoff de 2026-10-21): modos, iluminação do modo ativo e telemetria.
public partial class TrayFlyout : Window
{
    const double PreferredHeight = 864;   // 840 do painel + 12 de margem para a sombra em cima e embaixo

    readonly Vm vm;
    readonly FlyoutViewModel model;
    readonly ContextMenu moreMenu = new();
    bool hiding;
    DateTime hiddenAt;

    public TrayFlyout(Vm vm, Action<string> openWindowAt)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = model = new FlyoutViewModel(vm);

        void Open(string page) { HideAnimated(); openWindowAt(page); }
        OpenButton.Click += (_, _) => Open("modos");
        OpenMain.Click += (_, _) => Open("modos");
        SettingsButton.Click += (_, _) => Open("ajustes");
        RgbButton.Click += (_, _) => Open("iluminacao");

        // "Mais" abre um menu com os outros efeitos; não é um quarto efeito
        foreach (var o in model.MoreEffects)
        {
            var item = new MenuItem { Header = o.Label };
            item.Click += (_, _) => model.PickMoreCommand.Execute(o);
            moreMenu.Items.Add(item);
        }
        moreMenu.Opened += (_, _) => { foreach (MenuItem mi in moreMenu.Items) mi.IsChecked = (string)mi.Header == model.MoreLabel; };
        MoreButton.Click += (_, _) =>
        {
            moreMenu.PlacementTarget = MoreButton;
            moreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            moreMenu.IsOpen = true;
        };

        Deactivated += (_, _) => { if (!moreMenu.IsOpen) HideAnimated(); };
        vm.Lighting.SweepRequested += Keyboard.Sweep;
        IsVisibleChanged += (_, _) =>
        {
            App.FlyoutVisible = IsVisible;
            vm.State.Sensors.UiVisible = IsVisible || App.ShellVisible;
            model.SetVisible(IsVisible);
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
        // em tela baixa (ou escala alta) o painel encolhe e a parte do meio rola
        Height = Math.Min(PreferredHeight, area.Height);
        Left = area.Right - Width;   // a margem de 12 px do Root faz o respiro até a borda
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
        moreMenu.IsOpen = false;
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
