using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AcerMode.App.Theme;

namespace AcerMode.App.Controls;

// Medidor em arco de 240°: trilho, valor (accent, âmbar acima do aviso, vermelho acima de 85) e número.
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(ArcGauge), new PropertyMetadata(null, (d, _) => ((ArcGauge)d).OnValue()));

    static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(ArcGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ArcGauge), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Minimum { get; set; } = 30;
    public double Maximum { get; set; } = 100;
    public double WarnAt { get; set; } = 80;
    public double DangerAt { get; set; } = 85;
    public string Unit { get; set; } = "°C";

    const double Start = 150, Sweep = 240;

    public ArcGauge() => AccentManager.Changed += _ => InvalidateVisual();

    void OnValue()
    {
        if (Value is not { } v) { BeginAnimation(ShownProperty, null); InvalidateVisual(); return; }
        if (!Motion.Enabled) { BeginAnimation(ShownProperty, null); SetValue(ShownProperty, v); return; }
        BeginAnimation(ShownProperty, new DoubleAnimation(v, Motion.D(400)) { EasingFunction = Motion.EaseOut });
    }

    static Point On(Point c, double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    static Geometry Arc(Point c, double r, double from, double sweep)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(On(c, r, from), false, false);
            ctx.ArcTo(On(c, r, from + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 40) return;
        const double thick = 10;
        var c = new Point(ActualWidth / 2, ActualHeight / 2 + size * 0.06);
        double r = size / 2 - thick;

        var track = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, track, Arc(c, r, Start, Sweep));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), 12,
            (Brush)FindResource("Text2Brush"), dpi);

        if (Value is null)
        {
            var dash = new FormattedText("—", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Display"), size * 0.2,
                (Brush)FindResource("Text2Brush"), dpi);
            dc.DrawText(dash, new Point(c.X - dash.Width / 2, c.Y - dash.Height / 2));
            dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y + r * 0.62));
            return;
        }

        double v = (double)GetValue(ShownProperty);
        double k = Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1);
        Color col = v > DangerAt ? Color.FromRgb(0xFF, 0x6B, 0x5E) : v >= WarnAt ? Color.FromRgb(0xF5, 0xB8, 0x3D) : AccentManager.Shown;
        if (k > 0.005)
        {
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(0x30, col.R, col.G, col.B)), thick + 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var arc = Arc(c, r, Start, Sweep * k);
            dc.DrawGeometry(null, glow, arc);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(col), thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }

        var num = new FormattedText(Math.Round(v).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size * 0.24,
            (Brush)FindResource("Text1Brush"), dpi);
        var unit = new FormattedText(Unit, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), size * 0.08,
            (Brush)FindResource("Text2Brush"), dpi);
        double total = num.Width + 3 + unit.Width;
        double y = c.Y - num.Height / 2 - size * 0.02;
        dc.DrawText(num, new Point(c.X - total / 2, y));
        dc.DrawText(unit, new Point(c.X - total / 2 + num.Width + 3, y + num.Height - unit.Height - size * 0.035));
        dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y + r * 0.62));
    }
}
