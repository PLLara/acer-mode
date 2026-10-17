using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AcerMode.App.Pages;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;

namespace AcerMode.App.Shell;

// Janela principal: trilho lateral, páginas com transição e faixa de avisos.
public partial class ShellWindow : Window
{
    readonly Vm vm;
    readonly Dictionary<string, FrameworkElement> pages = new();
    readonly Dictionary<string, RadioButton> nav;
    string current = "";
    bool hiding;

    public ShellWindow(Vm vm)
    {
        this.vm = vm;
        InitializeComponent();
        nav = new()
        {
            ["modos"] = NavModos, ["iluminacao"] = NavIluminacao, ["sistema"] = NavSistema,
            ["ventoinhas"] = NavVentoinhas, ["ajustes"] = NavAjustes,
        };
        foreach (var (key, button) in nav) button.Checked += (_, _) => Navigate(key);

        SourceInitialized += (_, _) => Backdrop.Apply(this, BackdropKind.Mica);
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => HideAnimated();
        BannerClose.Click += (_, _) => vm.State.Banner = null;
        vm.State.PropertyChanged += OnState;
        // Minimizada conta como escondida: leitura completa pela BIOS só com a janela na tela
        // (cada leitura congela núcleos; com a janela minimizada a pessoa provavelmente está jogando).
        void SyncVisible()
        {
            App.ShellVisible = IsVisible && WindowState != WindowState.Minimized;
            vm.State.Sensors.UiVisible = App.ShellVisible || App.FlyoutVisible;
        }
        IsVisibleChanged += (_, _) => SyncVisible();
        StateChanged += (_, _) => SyncVisible();
        Loaded += (_, _) => MoveIndicator(animate: false);

        Navigate("modos");
        ShowBanner(vm.State.Banner);
    }

    public FrameworkElement RootElement => Root;

    void OnState(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.Banner)) ShowBanner(vm.State.Banner);
    }

    void ShowBanner(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            var fade = Motion.To(0, 160);
            fade.Completed += (_, _) => { if (string.IsNullOrEmpty(vm.State.Banner)) BannerBox.Visibility = Visibility.Collapsed; };
            BannerBox.BeginAnimation(OpacityProperty, fade);
            return;
        }
        BannerText.Text = text;
        BannerBox.ToolTip = text;
        BannerBox.Visibility = Visibility.Visible;
        BannerBox.BeginAnimation(OpacityProperty, Motion.To(1, 200));
    }

    public void Navigate(string key)
    {
        if (key == current || !nav.ContainsKey(key)) return;
        if (nav[key].IsChecked != true) { nav[key].IsChecked = true; return; }   // o Checked chama de volta
        current = key;

        if (!pages.TryGetValue(key, out var page)) pages[key] = page = PageFactory.Create(key, vm);
        PageTitle.Text = PageFactory.Title(key);

        var old = PageHost.Content as FrameworkElement;
        PageHost.Content = page;
        if (old is not null && Motion.Enabled)
        {
            page.RenderTransform = new TranslateTransform(12, 0);
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, Motion.FromTo(0, 1, 220));
            ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Motion.FromTo(12, 0, 220));
        }
        if (page is IAnimatedPage ap) Dispatcher.BeginInvoke(ap.PlayEntrance, System.Windows.Threading.DispatcherPriority.Loaded);
        MoveIndicator(animate: true);
    }

    public void SyncIndicator() => MoveIndicator(animate: false);

    void MoveIndicator(bool animate)
    {
        if (!nav.TryGetValue(current, out var b) || b.ActualHeight == 0) return;
        var p = b.TransformToAncestor(Rail).Transform(new Point(0, (b.ActualHeight - Indicator.Height) / 2));
        if (animate) IndicatorY.BeginAnimation(TranslateTransform.YProperty, Motion.To(p.Y, 220));
        else { IndicatorY.BeginAnimation(TranslateTransform.YProperty, null); IndicatorY.Y = p.Y; }
    }

    // ---- entrada e saída ----
    // A janela (Mica) não aceita opacidade própria; o DWM anima a janela e o conteúdo
    // entra com fade + subida de 14 px por cima.
    public void ShowAnimated()
    {
        bool wasHiding = hiding;
        hiding = false;
        if (!IsVisible || wasHiding)
        {
            if (!IsVisible) Show();
            var rise = new TranslateTransform(0, 14);
            Root.RenderTransform = rise;
            Root.BeginAnimation(OpacityProperty, Motion.FromTo(0, 1, 240));
            rise.BeginAnimation(TranslateTransform.YProperty, Motion.FromTo(14, 0, 240));
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (PageHost.Content is IAnimatedPage ap) Dispatcher.BeginInvoke(ap.PlayEntrance, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void HideAnimated()
    {
        if (!IsVisible || hiding) return;
        hiding = true;
        var fade = Motion.To(0, 160);
        fade.Completed += (_, _) => { if (hiding) { Hide(); hiding = false; } };
        Root.BeginAnimation(OpacityProperty, fade);
        var drop = new TranslateTransform(0, 0);
        Root.RenderTransform = drop;
        drop.BeginAnimation(TranslateTransform.YProperty, Motion.To(10, 160));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) HideAnimated();
        // Só na demonstração: Ctrl+Shift+H simula 90 °C para testar a proteção das ventoinhas.
        if (e.Key == Key.H && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && vm.State.Hardware is Core.FakeHardware fake)
            fake.NextReading = new Core.SensorReading(90, 80, 50, 4000, 3800, DateTime.Now);
    }

    // Fechar (Alt+F4) só esconde; "Sair" na bandeja encerra de verdade.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.ReallyExiting) { e.Cancel = true; HideAnimated(); }
        base.OnClosing(e);
    }
}
