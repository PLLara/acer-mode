using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;

namespace AcerMode.App.Controls;

// Teclado de 4 zonas desenhado em vetor. Simula o efeito escolhido enquanto está visível,
// varre as zonas da esquerda para a direita quando o modo troca e aceita clique por zona.
public sealed class KeyboardPreview : FrameworkElement
{
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(LightingProfile), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(new LightingProfile(), FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((KeyboardPreview)d).OnProfile((LightingProfile?)e.OldValue)));

    public static readonly DependencyProperty SelectedZoneProperty = DependencyProperty.Register(
        nameof(SelectedZone), typeof(int), typeof(KeyboardPreview),
        new FrameworkPropertyMetadata(-2, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(KeyboardPreview), new PropertyMetadata(false));

    public LightingProfile Profile { get => (LightingProfile)GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    public int SelectedZone { get => (int)GetValue(SelectedZoneProperty); set => SetValue(SelectedZoneProperty, value); }   // -1 todas, -2 nenhuma
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    const int Zones = 4, Rows = 5, Cols = 4;
    readonly Stopwatch clock = Stopwatch.StartNew();
    Color[] sweepFrom = new Color[Zones];
    double sweepStart = double.NegativeInfinity;
    int hoverZone = -1;
    bool rendering;

    public KeyboardPreview()
    {
        IsVisibleChanged += (_, _) => UpdateLoop();
        Unloaded += (_, _) => StopLoop();
        Loaded += (_, _) => UpdateLoop();
        AccentManager.Changed += _ => InvalidateVisual();
    }

    // Chamado quando o modo ativo troca: as zonas mudam uma depois da outra.
    public void Sweep()
    {
        if (!Motion.Enabled) return;
        for (int i = 0; i < Zones; i++) sweepFrom[i] = lastDrawn[i];
        sweepStart = clock.Elapsed.TotalSeconds;
        UpdateLoop();
    }

    void OnProfile(LightingProfile? old) => UpdateLoop();

    bool NeedsLoop =>
        IsVisible && Motion.Enabled &&
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
    readonly Color[] lastDrawn = new Color[Zones];

    Color ZoneColor(int z, double t)
    {
        var p = Profile.Normalized();
        double speed = 0.15 + p.Speed * 0.12;           // ciclos por segundo
        double dir = p.Direction == KbDirection.LeftToRight ? -1 : 1;
        Color baseC = LightingBrushes.ToColor(p.Effect == KbEffect.Static ? p.Zones[z] : p.Color);
        Color c = p.Effect switch
        {
            KbEffect.Static => baseC,
            KbEffect.Breathing => ColorMath.Scale(baseC, 0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(t * speed * Math.PI * 2))),
            KbEffect.Neon => ColorMath.FromHsv(t * speed * 0.5, 1, 1),
            KbEffect.Wave => ColorMath.FromHsv(t * speed * 0.5 + dir * z * 0.18, 1, 1),
            KbEffect.Shifting => (int)Math.Floor(t * speed * 4 * dir % Zones + Zones) % Zones == z ? baseC : ColorMath.Scale(baseC, 0.15),
            KbEffect.Zoom => ColorMath.Scale(baseC, 0.2 + 0.8 * Pulse(t * speed - Math.Abs(z - 1.5) * 0.15)),
            KbEffect.Meteor => ColorMath.Scale(baseC, Math.Max(0.08, 1 - Math.Abs((t * speed * 2 % 1.6) * Zones - z - 0.5) / 1.6)),
            KbEffect.Twinkling => ColorMath.Scale(baseC, 0.2 + 0.8 * Math.Max(0, Math.Sin(t * speed * 9 + z * 2.1) * Math.Sin(t * speed * 4 + z * 1.3))),
            _ => baseC,
        };
        return c;
    }

    static double Pulse(double x) { x = x % 1; if (x < 0) x += 1; return Math.Pow(Math.Sin(x * Math.PI), 2); }

    // ---- desenho ----
    Rect KeyArea => new(14, 14, Math.Max(0, ActualWidth - 28), Math.Max(0, ActualHeight - 28));

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 30) return;
        double t = clock.Elapsed.TotalSeconds;
        var p = Profile.Normalized();
        double bright = 0.2 + 0.8 * p.Brightness / 100.0;

        var chassis = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1C));
        dc.DrawRoundedRectangle(chassis, new Pen(new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), 1), new Rect(0.5, 0.5, w - 1, h - 1), 14, 14);

        var area = KeyArea;
        double zoneW = area.Width / Zones;
        double gap = Math.Max(3, area.Width / 140);
        double keyW = (zoneW - gap * Cols) / Cols, keyH = (area.Height - gap * (Rows - 1)) / Rows;

        for (int z = 0; z < Zones; z++)
        {
            Color c = ZoneColor(z, t);
            double sweepT = (t - sweepStart - z * 0.06) / 0.2;
            if (sweepT < 1) c = ColorMath.Lerp(sweepFrom[z], c, Math.Clamp(sweepT, 0, 1));
            lastDrawn[z] = c;
            Color lit = ColorMath.Scale(c, bright);

            // brilho difuso embaixo das teclas
            var zr = new Rect(area.X + z * zoneW, area.Y, zoneW, area.Height);
            var glow = new RadialGradientBrush(Color.FromArgb((byte)(110 * bright), lit.R, lit.G, lit.B), Color.FromArgb(0, lit.R, lit.G, lit.B))
            { RadiusX = 0.75, RadiusY = 0.9 };
            dc.DrawRectangle(glow, null, Rect.Inflate(zr, 6, 8));

            var keyFill = new SolidColorBrush(Color.FromArgb(0xF2, 0x1C, 0x21, 0x2C));
            var legend = new SolidColorBrush(Color.FromArgb((byte)(80 + 175 * bright), lit.R, lit.G, lit.B));
            var rim = new Pen(new SolidColorBrush(Color.FromArgb((byte)(60 + 120 * bright), lit.R, lit.G, lit.B)), 1);
            for (int r = 0; r < Rows; r++)
            {
                int cols = Cols;
                double kw = keyW;
                if (r == Rows - 1 && (z == 1 || z == 2)) { cols = 1; kw = zoneW - gap; }   // barra de espaço
                for (int k = 0; k < cols; k++)
                {
                    var kr = new Rect(zr.X + gap / 2 + k * (kw + gap), area.Y + r * (keyH + gap), kw, keyH);
                    dc.DrawRoundedRectangle(keyFill, rim, kr, 3, 3);
                    var lr = new Rect(kr.X + kr.Width * 0.3, kr.Y + kr.Height * 0.38, kr.Width * 0.4, Math.Max(1.5, kr.Height * 0.14));
                    if (cols > 1) dc.DrawRoundedRectangle(legend, null, lr, 1, 1);
                }
            }

            bool selected = SelectedZone == z;
            if (Interactive && (selected || hoverZone == z))
            {
                var ring = new Pen(new SolidColorBrush(selected ? AccentManager.Shown : Color.FromArgb(0x40, 255, 255, 255)), selected ? 2 : 1);
                dc.DrawRoundedRectangle(null, ring, Rect.Inflate(zr, -1, 3), 8, 8);
            }
        }
    }

    // ---- interação ----
    public event Action<int>? ZoneClicked;

    int ZoneAt(Point pt)
    {
        var a = KeyArea;
        if (!a.Contains(pt)) return -1;
        return Math.Clamp((int)((pt.X - a.X) / (a.Width / Zones)), 0, Zones - 1);
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
