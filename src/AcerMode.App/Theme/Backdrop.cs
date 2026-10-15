using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AcerMode.App.Theme;

public enum BackdropKind { Mica = 2, Acrylic = 3 }

// Mica/acrílico do Windows 11 numa janela WPF sem borda (WindowChrome com GlassFrameThickness -1).
public static class Backdrop
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    const int DarkMode = 20, CornerPreference = 33, SystemBackdrop = 38;

    public static void Apply(Window w, BackdropKind kind)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        int on = 1, round = 2, k = (int)kind;
        DwmSetWindowAttribute(h, DarkMode, ref on, 4);
        DwmSetWindowAttribute(h, CornerPreference, ref round, 4);
        DwmSetWindowAttribute(h, SystemBackdrop, ref k, 4);
        if (HwndSource.FromHwnd(h) is { CompositionTarget: { } ct }) ct.BackgroundColor = Colors.Transparent;
    }
}
