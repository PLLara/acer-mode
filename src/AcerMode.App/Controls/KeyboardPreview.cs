using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;

namespace AcerMode.App.Controls;

// Teclado do PHN16-72 desenhado em vetor (KeyboardLayout): base escura, legenda clara,
// contorno na cor da zona e um brilho suave. Simula o efeito escolhido enquanto está visível,
// varre as zonas da esquerda para a direita quando o modo troca e aceita clique por zona.
public sealed class KeyboardPreview : FrameworkElement
{
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(LightingProfile), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(new LightingProfile(), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((KeyboardPreview)d).UpdateLoop()));

    public static readonly DependencyProperty SelectedZoneProperty = DependencyProperty.Register(
        nameof(SelectedZone), typeof(int), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(-2, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(KeyboardPreview), new PropertyMetadata(false));

    public LightingProfile Profile { get => (LightingProfile)GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    public int SelectedZone { get => (int)GetValue(SelectedZoneProperty); set => SetValue(SelectedZoneProperty, value); }   // -1 todas, -2 nenhuma
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    const int Zones = KeyboardLayout.Zones;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Color[] sweepFrom = new Color[Zones];
    readonly Color[] lastDrawn = new Color[Zones];
    double sweepStart = double.NegativeInfinity;
    int hoverZone = -1;
    bool rendering;

    // legendas em cache por tamanho de fonte (criar FormattedText a cada quadro custa caro)
    readonly Dictionary<(string, double), FormattedText> legends = new();
    static readonly Brush LegendBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA)));
    static readonly Brush KeyFill = Freeze(new SolidColorBrush(Color.FromRgb(0x0C, 0x12, 0x16)));
    static readonly Brush DeckFill = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0F, 0x13)));
    static readonly Pen DeckPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), 1));
    static readonly Typeface Face = new("Segoe UI");

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public KeyboardPreview()
    {
        IsVisibleChanged += (_, _) => UpdateLoop();
        Unloaded += (_, _) => StopLoop();
        Loaded += (_, _) => UpdateLoop();
        AccentManager.Changed += _ => InvalidateVisual();
    }

    public void Sweep()
    {
        if (!Motion.Enabled) return;
        Array.Copy(lastDrawn, sweepFrom, Zones);
        sweepStart = clock.Elapsed.TotalSeconds;
        UpdateLoop();
    }

    bool NeedsLoop => IsVisible && Motion.Enabled &&
        (Profile.Effect != KbEffect.Static || clock.Elapsed.TotalSeconds - sweepStart < 0.6);

    void UpdateLoop()
    {
        if (NeedsLoop && !rendering) { CompositionTarget.Rendering += OnFrame; rendering = true; }
        else if (!NeedsLoop) StopLoop();
        InvalidateVisual();
    }

    void StopLoop()
    {
        if (rendering) { CompositionTarget.Rendering -= OnFrame; rendering = false; }
    }

    void OnFrame(object? s, EventArgs e)
    {
        InvalidateVisual();
        if (!NeedsLoop) StopLoop();
    }

    // ---- cor de cada zona no instante t ----
    Color ZoneColor(LightingProfile p, int z, double t)
    {
        double speed = 0.15 + p.Speed * 0.12;   // ciclos por segundo
        double dir = p.Direction == KbDirection.LeftToRight ? -1 : 1;
        Color baseC = LightingBrushes.ToColor(p.Effect == KbEffect.Static ? p.Zones[z] : p.Color);
        return p.Effect switch
        {
            KbEffect.Static => baseC,
            KbEffect.Breathing => ColorMath.Scale(baseC, 0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(t * Math.PI * 2 / 2.4))),
            KbEffect.Neon => ColorMath.FromHsv(t * speed * 0.5, 1, 1),
            KbEffect.Wave => ColorMath.FromHsv(t * speed * 0.5 + dir * z * 0.18, 1, 1),
            KbEffect.Shifting => (int)Math.Floor(t * speed * 4 * dir % Zones + Zones) % Zones == z ? baseC : ColorMath.Scale(baseC, 0.15),
            KbEffect.Zoom => ColorMath.Scale(baseC, 0.2 + 0.8 * Pulse(t * speed - Math.Abs(z - 1.5) * 0.15)),
            KbEffect.Meteor => ColorMath.Scale(baseC, Math.Max(0.08, 1 - Math.Abs((t * speed * 2 % 1.6) * Zones - z - 0.5) / 1.6)),
            KbEffect.Twinkling => ColorMath.Scale(baseC, 0.2 + 0.8 * Math.Max(0, Math.Sin(t * speed * 9 + z * 2.1) * Math.Sin(t * speed * 4 + z * 1.3))),
            _ => baseC,
        };
    }

    static double Pulse(double x) { x %= 1; if (x < 0) x += 1; return Math.Pow(Math.Sin(x * Math.PI), 2); }

    // ---- geometria: o teclado ocupa a área mantendo a proporção ----
    (double Unit, Point Origin) Fit()
    {
        const double pad = 12;
        double w = Math.Max(0, ActualWidth - 2 * pad), h = Math.Max(0, ActualHeight - 2 * pad);
        double unit = Math.Min(w / KeyboardLayout.Width, h / KeyboardLayout.Height);
        double ox = (ActualWidth - unit * KeyboardLayout.Width) / 2, oy = (ActualHeight - unit * KeyboardLayout.Height) / 2;
        return (unit, new Point(ox, oy));
    }

    FormattedText Legend(string text, double size, double dpi)
    {
        var key = (text, Math.Round(size, 1));
        if (!legends.TryGetValue(key, out var ft))
            legends[key] = ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, key.Item2, LegendBrush, dpi);
        return ft;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 60 || ActualHeight < 30) return;
        var (u, o) = Fit();
        if (u < 2) return;
        double t = clock.Elapsed.TotalSeconds;
        var p = Profile.Normalized();
        double bright = 0.2 + 0.8 * p.Brightness / 100.0;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // base do teclado
        var deck = new Rect(o.X - 0.25 * u, o.Y - 0.25 * u, (KeyboardLayout.Width + 0.5) * u, (KeyboardLayout.Height + 0.5) * u);
        dc.DrawRoundedRectangle(DeckFill, DeckPen, deck, 0.3 * u, 0.3 * u);

        // cor final de cada zona (com a varredura de troca de modo)
        var lit = new Color[Zones];
        var rim = new Pen[Zones];
        var glow = new Pen[Zones];
        for (int z = 0; z < Zones; z++)
        {
            Color c = ZoneColor(p, z, t);
            double sweepT = (t - sweepStart - z * 0.06) / 0.2;
            if (sweepT < 1) c = ColorMath.Lerp(sweepFrom[z], c, Math.Clamp(sweepT, 0, 1));
            lastDrawn[z] = c;
            lit[z] = ColorMath.Scale(c, bright);
            rim[z] = new Pen(new SolidColorBrush(lit[z]), Math.Max(1, u * 0.045));
            glow[z] = new Pen(new SolidColorBrush(Color.FromArgb((byte)(70 * bright), lit[z].R, lit[z].G, lit[z].B)), Math.Max(2.5, u * 0.16));
        }

        double gap = 0.1 * u, radius = 0.14 * u;
        double small = Math.Max(7, u * 0.24), big = Math.Max(8, u * 0.3);
        // primeiro todos os brilhos, depois as teclas: o brilho fica atrás e não "suja" a vizinha
        foreach (var k in KeyboardLayout.Keys)
            dc.DrawRoundedRectangle(null, glow[k.Zone], KeyRect(k, o, u, gap), radius, radius);
        foreach (var k in KeyboardLayout.Keys)
        {
            var r = KeyRect(k, o, u, gap);
            dc.DrawRoundedRectangle(KeyFill, rim[k.Zone], r, radius, radius);
            bool word = k.Bottom.Length > 1 && k.Top.Length == 0;
            if (k.Top.Length > 0)
            {
                var top = Legend(k.Top, small, dpi);
                var bot = Legend(k.Bottom, small, dpi);
                dc.DrawText(top, new Point(r.X + (r.Width - top.Width) / 2, r.Y + r.Height * 0.12));
                dc.DrawText(bot, new Point(r.X + (r.Width - bot.Width) / 2, r.Bottom - bot.Height - r.Height * 0.1));
            }
            else if (k.Bottom.Length > 0)
            {
                var ft = Legend(k.Bottom, word ? small : big, dpi);
                dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
            }
        }

        // zona selecionada / em foco
        if (Interactive)
            for (int z = 0; z < Zones; z++)
            {
                if (SelectedZone != z && hoverZone != z) continue;
                var zr = ZoneRect(z, o, u);
                var pen = new Pen(new SolidColorBrush(SelectedZone == z ? AccentManager.Shown : Color.FromArgb(0x55, 255, 255, 255)), SelectedZone == z ? 2 : 1);
                dc.DrawRoundedRectangle(null, pen, zr, 0.25 * u, 0.25 * u);
            }
    }

    static Rect KeyRect(KeyCap k, Point o, double u, double gap) =>
        new(o.X + k.Bounds.X * u + gap / 2, o.Y + k.Bounds.Y * u + gap / 2, Math.Max(1, k.Bounds.Width * u - gap), Math.Max(1, k.Bounds.Height * u - gap));

    static Rect ZoneRect(int z, Point o, double u)
    {
        double zw = KeyboardLayout.Width / Zones * u;
        return new Rect(o.X + z * zw - 0.05 * u, o.Y - 0.12 * u, zw + 0.1 * u, KeyboardLayout.Height * u + 0.24 * u);
    }

    // ---- interação ----
    public event Action<int>? ZoneClicked;

    int ZoneAt(Point pt)
    {
        var (u, o) = Fit();
        var area = new Rect(o.X, o.Y, KeyboardLayout.Width * u, KeyboardLayout.Height * u);
        if (u < 2 || !area.Contains(pt)) return -1;
        return Math.Clamp((int)((pt.X - o.X) / (KeyboardLayout.Width * u / Zones)), 0, Zones - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Interactive) return;
        int z = ZoneAt(e.GetPosition(this));
        Cursor = z >= 0 ? Cursors.Hand : null;
        if (z != hoverZone) { hoverZone = z; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverZone != -1) { hoverZone = -1; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!Interactive) return;
        int z = ZoneAt(e.GetPosition(this));
        if (z < 0) return;
        SelectedZone = z;
        ZoneClicked?.Invoke(z);
    }
}
