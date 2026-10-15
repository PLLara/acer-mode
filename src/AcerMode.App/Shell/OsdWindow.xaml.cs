using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AcerMode.App.Theme;
using AcerMode.Core;

namespace AcerMode.App.Shell;

// Aviso de modo: entra com "pop", fica 1,2 s, some com fade. Não rouba foco, não bloqueia
// cliques e fica fora do Alt-Tab. Uma troca durante a exibição só troca o conteúdo.
public partial class OsdWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    const int ExStyle = -20, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000, Layered = 0x80000;

    readonly DispatcherTimer hold = new() { Interval = TimeSpan.FromMilliseconds(1220) };
    bool fadingOut;

    public OsdWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(h, ExStyle).ToInt64();
            SetWindowLongPtr(h, ExStyle, new IntPtr(ex | Transparent | ToolWindow | NoActivate | Layered));
        };
        hold.Tick += (_, _) => { hold.Stop(); FadeOut(); };
    }

    public FrameworkElement RootElement => Root;

    public void SetMode(ThermalMode m)
    {
        var c = AccentManager.For(m);
        Ring.BorderBrush = new SolidColorBrush(c);
        Glow.Color = c;
        IconBadge.Background = new SolidColorBrush(AccentManager.WithAlpha(c, 0x2E));
        ModeIcon.Foreground = new SolidColorBrush(c);
        ModeIcon.Data = TryFindResource("Icon." + m) as Geometry;
        ModeName.Text = m.ToString();
    }

    public void Flash(ThermalMode m)
    {
        SetMode(m);
        bool alreadyShown = IsVisible && !fadingOut;
        fadingOut = false;
        if (!IsVisible)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Bottom - Height - 56;
            Show();
        }
        if (!alreadyShown)
        {
            Root.BeginAnimation(OpacityProperty, Motion.FromTo(0, 1, 180));
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.FromTo(0.86, 1, 220, Motion.Spring));
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.FromTo(0.86, 1, 220, Motion.Spring));
            Shift.BeginAnimation(TranslateTransform.YProperty, Motion.FromTo(14, 0, 180));
        }
        else
        {
            // já visível: um "tique" curto no lugar de repetir a entrada
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.FromTo(1.04, 1, 180, Motion.EaseOut));
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.FromTo(1.04, 1, 180, Motion.EaseOut));
        }
        hold.Stop();
        hold.Start();
    }

    void FadeOut()
    {
        fadingOut = true;
        var fade = Motion.To(0, 350);
        fade.Completed += (_, _) => { if (fadingOut) { Hide(); fadingOut = false; } };
        Root.BeginAnimation(OpacityProperty, fade);
        Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(8, 350));
    }
}
