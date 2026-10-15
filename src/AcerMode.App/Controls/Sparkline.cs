using System.Windows;
using System.Windows.Media;
using AcerMode.App.Theme;

namespace AcerMode.App.Controls;

// Histórico de 2 min: CPU no accent, GPU em cinza, área preenchida suave, linhas-guia a cada 10 °C.
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty Series1Property = DependencyProperty.Register(
        nameof(Series1), typeof(IReadOnlyList<double>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Series2Property = DependencyProperty.Register(
        nameof(Series2), typeof(IReadOnlyList<double>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Series1 { get => (IReadOnlyList<double>?)GetValue(Series1Property); set => SetValue(Series1Property, value); }
    public IReadOnlyList<double>? Series2 { get => (IReadOnlyList<double>?)GetValue(Series2Property); set => SetValue(Series2Property, value); }
    public double Min { get; set; } = 30;
    public double Max { get; set; } = 100;
    public int Capacity { get; set; } = 120;

    public Sparkline() => AccentManager.Changed += _ => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var guide = new Pen(new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)), 1);
        for (double t = Min + 10; t < Max; t += 10)
        {
            double y = Y(t, h);
            dc.DrawLine(guide, new Point(0, y), new Point(w, y));
        }
        Draw(dc, Series2, Color.FromRgb(0xA6, 0xAD, 0xBB), w, h, 0x10);
        Draw(dc, Series1, AccentManager.Shown, w, h, 0x26);
    }

    double Y(double v, double h) => h - Math.Clamp((v - Min) / (Max - Min), 0, 1) * h;

    void Draw(DrawingContext dc, IReadOnlyList<double>? s, Color c, double w, double h, byte fillAlpha)
    {
        if (s is null || s.Count < 2) return;
        double step = w / (Capacity - 1);
        double x0 = w - (s.Count - 1) * step;
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            l.BeginFigure(new Point(x0, Y(s[0], h)), false, false);
            a.BeginFigure(new Point(x0, h), true, true);
            a.LineTo(new Point(x0, Y(s[0], h)), false, false);
            for (int i = 1; i < s.Count; i++)
            {
                var p = new Point(x0 + i * step, Y(s[i], h));
                l.LineTo(p, true, true);
                a.LineTo(p, false, false);
            }
            a.LineTo(new Point(w, h), false, false);
        }
        var fill = new LinearGradientBrush(Color.FromArgb(fillAlpha, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(c), 2) { LineJoin = PenLineJoin.Round }, line);
    }
}
