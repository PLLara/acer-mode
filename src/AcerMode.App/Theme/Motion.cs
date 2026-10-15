using System.Windows;
using System.Windows.Media.Animation;

namespace AcerMode.App.Theme;

// Linguagem única de movimento: 150–250 ms, ease-out; desligada se o Windows pedir.
public static class Motion
{
    public static bool ForceOff { get; set; }   // capturas fora da tela
    public static bool Enabled => !ForceOff && SystemParameters.ClientAreaAnimation;
    public static int Ms(int ms) => Enabled ? ms : 0;
    public static Duration D(int ms) => new(TimeSpan.FromMilliseconds(Ms(ms)));

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseInOut = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });

    static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }

    public static DoubleAnimation To(double to, int ms, IEasingFunction? ease = null, int delayMs = 0) =>
        new(to, D(ms)) { EasingFunction = ease ?? EaseOut, BeginTime = TimeSpan.FromMilliseconds(Enabled ? delayMs : 0) };

    public static DoubleAnimation FromTo(double from, double to, int ms, IEasingFunction? ease = null, int delayMs = 0) =>
        new(from, to, D(ms)) { EasingFunction = ease ?? EaseOut, BeginTime = TimeSpan.FromMilliseconds(Enabled ? delayMs : 0) };
}
