using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;

namespace AcerMode.App.Controls;

// Cartão de modo: hover levanta, clique afunda e solta um ripple, ativo ganha anel e brilho.
public partial class ModeCard : UserControl
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ModeCard));

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    ModeCardVm? vm;
    bool pressed;

    public ModeCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        SizeChanged += (_, _) => Inner.Clip = new RectangleGeometry(new Rect(Inner.RenderSize), 11, 11);
        IsVisibleChanged += (_, _) => UpdatePulse();
    }

    void Attach()
    {
        if (vm is not null) vm.PropertyChanged -= OnVm;
        vm = DataContext as ModeCardVm;
        if (vm is null) return;
        vm.PropertyChanged += OnVm;
        var c = vm.Accent;
        Ring.BorderBrush = new SolidColorBrush(c);
        Glow.Color = c;
        Tint.Background = new SolidColorBrush(AccentManager.WithAlpha(c, 0x1F));
        ActiveTag.Foreground = new SolidColorBrush(c);
        ApplyActive(animate: false);
    }

    void OnVm(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModeCardVm.IsActive)) ApplyActive(animate: true);
    }

    void ApplyActive(bool animate)
    {
        if (vm is null) return;
        double to = vm.IsActive ? 1 : 0;
        foreach (var el in new UIElement[] { Ring, Tint, ActiveTag })
        {
            if (animate && Motion.Enabled) el.BeginAnimation(OpacityProperty, Motion.To(to, 300));
            else { el.BeginAnimation(OpacityProperty, null); el.Opacity = to; }
        }
        ModeIcon.Foreground = vm.IsActive ? new SolidColorBrush(vm.Accent) : (Brush)FindResource("Text2Brush");
        IconBadge.Background = vm.IsActive ? new SolidColorBrush(AccentManager.WithAlpha(vm.Accent, 0x2E)) : (Brush)FindResource("Surface2Brush");
        UpdatePulse();
    }

    // Turbo ativo "respira" enquanto o cartão está visível.
    void UpdatePulse()
    {
        bool pulse = vm is { IsActive: true, Mode: ThermalMode.Turbo } && IsVisible && Motion.Enabled;
        if (pulse)
            Glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.3, 0.8, TimeSpan.FromMilliseconds(1600))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Motion.EaseInOut });
        else
            Glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        ScaleTo(1.01, 150);
        Card.BorderBrush = (Brush)FindResource("BorderStrongBrush");
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        pressed = false;
        ScaleTo(1, 150);
        Card.BorderBrush = (Brush)FindResource("BorderBrush");
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        pressed = true;
        ScaleTo(0.98, 80);
        Ripple(e.GetPosition(Inner));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!pressed) return;
        pressed = false;
        ScaleTo(1.01, 150);
        if (vm is not null && Command?.CanExecute(vm.Mode) == true) Command.Execute(vm.Mode);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space && vm is not null) { Command?.Execute(vm.Mode); e.Handled = true; }
    }

    void ScaleTo(double s, int ms)
    {
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(s, ms));
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(s, ms));
    }

    void Ripple(Point p)
    {
        if (!Motion.Enabled || vm is null) return;
        double r = Math.Max(Inner.ActualWidth, Inner.ActualHeight);
        var dot = new Ellipse
        {
            Width = r, Height = r, Fill = new SolidColorBrush(AccentManager.WithAlpha(vm.Accent, 0x59)),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0, 0), Opacity = 1,
        };
        Canvas.SetLeft(dot, p.X - r / 2);
        Canvas.SetTop(dot, p.Y - r / 2);
        RippleLayer.Children.Add(dot);
        var st = (ScaleTransform)dot.RenderTransform;
        var grow = Motion.FromTo(0, 2.2, 450);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = Motion.FromTo(1, 0, 450);
        fade.Completed += (_, _) => RippleLayer.Children.Remove(dot);
        dot.BeginAnimation(OpacityProperty, fade);
    }
}
