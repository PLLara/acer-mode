using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcerMode.App.Shell;
using AcerMode.App.Theme;
using AcerMode.Core;

namespace AcerMode.App.Startup;

// Capturas fora da tela sobre hardware simulado, para conferir o visual sem UAC.
//   --shot <página|flyout> <arquivo.png> [modo] [escala]
//   --shot-osd <arquivo.png> <modo> [escala]
public static class Shots
{
    public static async Task<int> RunAsync(string[] args)
    {
        Motion.ForceOff = true;
        bool osd = args[0] == "--shot-osd";
        string target = osd ? "osd" : Arg(args, 1, "modos");
        string file = Path.GetFullPath(Arg(args, osd ? 1 : 2, "shot.png"));
        ThermalModes.TryParse(Arg(args, osd ? 2 : 3, "Balanced"), out var mode);
        double scale = double.TryParse(Arg(args, osd ? 3 : 4, "1.5"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.5;

        var hw = new FakeHardware();
        await hw.SetModeAsync(mode);
        var comp = await Composition.CreateAsync(hw, demo: true);
        if (target == "ventoinhas")
        {
            comp.State.Settings.Profile(mode).Fans = new FanSettings(FanMode.Custom, 55, 65);
            comp.State.NotifyProfilesChanged();
        }
        for (int i = 0; i < 90; i++) comp.State.Sensors.History.Add(await hw.ReadSensorsAsync() with { CpuTempC = 70 + (int)(8 * Math.Sin(i / 9.0)), GpuTempC = 60 + (int)(6 * Math.Sin(i / 12.0 + 1)) });
        comp.State.Latest = comp.State.Sensors.History.Latest;

        FrameworkElement root;
        Size size;
        switch (target)
        {
            default:
                var w = new ShellWindow(comp.Vm);
                w.Navigate(target);
                root = w.RootElement;
                size = new Size(w.Width, w.Height);
                root.Measure(size);
                root.Arrange(new Rect(size));
                w.SyncIndicator();
                break;
        }
        Save(root, size, scale, file);
        return 0;
    }

    static string Arg(string[] a, int i, string fallback) => a.Length > i ? a[i] : fallback;

    static void Save(FrameworkElement root, Size size, double scale, string file)
    {
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.FindResource("Bg0Brush"), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(size));
        }
        var bmp = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
