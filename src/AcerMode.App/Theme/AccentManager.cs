using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AcerMode.Core;

namespace AcerMode.App.Theme;

// Uma cor de destaque para o app inteiro, que acompanha o modo ativo com cross-fade.
// Pincéis dentro dos recursos do app ficam selados; por isso o cross-fade troca, a cada
// quadro, pincéis novos (congelados) e quem usa DynamicResource acompanha.
public static class AccentManager
{
    public static Color For(ThermalMode m) => m switch
    {
        ThermalMode.Quiet => Color.FromRgb(0x22, 0xD3, 0xEE),
        ThermalMode.Performance => Color.FromRgb(0x8B, 0x5C, 0xF6),
        ThermalMode.Turbo => Color.FromRgb(0xFF, 0x4D, 0x1F),
        ThermalMode.Eco => Color.FromRgb(0x22, 0xC5, 0x5E),
        _ => Color.FromRgb(0x3B, 0x82, 0xF6),
    };

    public static ThermalMode Current { get; private set; } = ThermalMode.Balanced;
    public static Color Shown { get; private set; } = For(ThermalMode.Balanced);

    static ResourceDictionary? resources;
    static Color from, to;
    static readonly Stopwatch clock = new();
    static bool ticking;
    const double DurationMs = 300;

    public static event Action<ThermalMode>? Changed;

    public static void Install(ResourceDictionary r)
    {
        resources = r;
        Write(Shown);
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static void Apply(ThermalMode m, bool animate)
    {
        Current = m;
        to = For(m);
        Changed?.Invoke(m);
        if (!animate || !Motion.Enabled) { Stop(); Write(to); return; }
        from = Shown;
        clock.Restart();
        if (!ticking) { CompositionTarget.Rendering += Tick; ticking = true; }
    }

    static void Tick(object? sender, EventArgs e)
    {
        double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / DurationMs);
        double k = 1 - Math.Pow(1 - t, 3);   // ease-out cúbico
        Write(Lerp(from, to, k));
        if (t >= 1) Stop();
    }

    static void Stop()
    {
        if (ticking) { CompositionTarget.Rendering -= Tick; ticking = false; }
    }

    static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static void Write(Color c)
    {
        Shown = c;
        if (resources is null) return;
        resources["AccentColor"] = c;
        resources["AccentBrush"] = Frozen(c);
        resources["AccentSoftBrush"] = Frozen(WithAlpha(c, 0x29));
        resources["AccentGlowBrush"] = Frozen(WithAlpha(c, 0x8C));
    }

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
