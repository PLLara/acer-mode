using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AcerMode.App.Controls;

// Roda HSV (matiz no ângulo, saturação no raio, valor fixo em 1: o brilho é outro controle).
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorWheel),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ColorWheel)d).SyncFromColor()));

    public Color SelectedColor { get => (Color)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }

    WriteableBitmap? disc;
    int discSize;
    double hue, sat;
    bool internalSet;

    public ColorWheel()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
    }

    void SyncFromColor()
    {
        if (internalSet) return;
        var (h, s, _) = ColorMath.ToHsv(SelectedColor);
        if (s > 0.001) hue = h;
        sat = s;
    }

    double Radius => Math.Min(ActualWidth, ActualHeight) / 2 - 2;
    Point Center => new(ActualWidth / 2, ActualHeight / 2);

    WriteableBitmap Disc(int size)
    {
        if (disc is not null && discSize == size) return disc;
        var dpi = VisualTreeHelper.GetDpi(this);
        int px = (int)Math.Ceiling(size * dpi.DpiScaleX);
        var bmp = new WriteableBitmap(px, px, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);
        var buf = new int[px * px];
        double r = px / 2.0;
        for (int y = 0; y < px; y++)
            for (int x = 0; x < px; x++)
            {
                double dx = x + 0.5 - r, dy = y + 0.5 - r, d = Math.Sqrt(dx * dx + dy * dy);
                double a = Math.Clamp(r - d, 0, 1);   // borda suavizada
                if (a <= 0) continue;
                double h = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1;
                var c = ColorMath.FromHsv(h, Math.Min(1, d / r), 1);
                byte A = (byte)(a * 255);
                buf[y * px + x] = (A << 24) | ((byte)(c.R * a) << 16) | ((byte)(c.G * a) << 8) | (byte)(c.B * a);
            }
        bmp.WritePixels(new Int32Rect(0, 0, px, px), buf, px * 4, 0);
        bmp.Freeze();
        discSize = size;
        return disc = bmp;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double R = Radius;
        if (R < 8) return;
        var c = Center;
        int size = (int)(R * 2);
        dc.DrawImage(Disc(size), new Rect(c.X - R, c.Y - R, size, size));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), 1), c, R, R);

        double ang = hue * 2 * Math.PI;
        var thumb = new Point(c.X + Math.Cos(ang) * sat * R, c.Y + Math.Sin(ang) * sat * R);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, thumb, 10, 10);
        dc.DrawEllipse(new SolidColorBrush(SelectedColor), new Pen(Brushes.White, 2.5), thumb, 7.5, 7.5);
    }

    void PickAt(Point p)
    {
        var c = Center;
        double dx = p.X - c.X, dy = p.Y - c.Y;
        hue = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1;
        sat = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / Radius, 0, 1);
        Commit();
    }

    void Commit()
    {
        internalSet = true;
        SelectedColor = ColorMath.FromHsv(hue, sat, 1);
        internalSet = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        PickAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) PickAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    // Setas: 2° de matiz / 2 % de saturação.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: hue = (hue - 2 / 360.0 + 1) % 1; break;
            case Key.Right: hue = (hue + 2 / 360.0) % 1; break;
            case Key.Up: sat = Math.Min(1, sat + 0.02); break;
            case Key.Down: sat = Math.Max(0, sat - 0.02); break;
            default: return;
        }
        Commit();
        e.Handled = true;
    }
}
