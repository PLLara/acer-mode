using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AcerMode.App.Theme;
using AcerMode.App.ViewModels;
using AcerMode.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AcerMode.App.Shell;

// Ícone da bandeja (NotifyIcon do Windows) com selo na cor do modo; clique esquerdo abre o
// flyout, clique direito abre um menu WPF escuro.
public sealed class Tray : IDisposable
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    readonly Forms.NotifyIcon icon = new();
    readonly AppState state;
    readonly Action toggleFlyout, openWindow, exit;
    readonly Func<bool> getAutostart;
    readonly Action<bool> setAutostart;
    readonly Window menuHost;
    readonly Drawing.Icon baseIcon;
    Drawing.Icon? current;

    public Tray(AppState state, Action toggleFlyout, Action openWindow, Action exit, Func<bool> getAutostart, Action<bool> setAutostart)
    {
        this.state = state; this.toggleFlyout = toggleFlyout; this.openWindow = openWindow; this.exit = exit;
        this.getAutostart = getAutostart; this.setAutostart = setAutostart;

        using (var s = Application.GetResourceStream(new Uri("pack://application:,,,/AcerMode.ico")).Stream)
            baseIcon = new Drawing.Icon(s, 32, 32);

        // Janela invisível que recebe o foco, para o menu fechar ao clicar fora dele.
        menuHost = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent, Width = 1, Height = 1, Left = -10000, Top = -10000,
        };
        menuHost.Show();

        icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) toggleFlyout();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        UpdateIcon(state.ActiveMode);
        icon.Visible = true;
    }

    public void UpdateIcon(ThermalMode? mode)
    {
        var c = mode is { } m ? AccentManager.For(m) : System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80);
        using var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawIcon(baseIcon, new Drawing.Rectangle(0, 0, 32, 32));
            using var ring = new Drawing.SolidBrush(Drawing.Color.FromArgb(24, 24, 27));
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(c.R, c.G, c.B));
            g.FillEllipse(ring, 17, 17, 15, 15);
            g.FillEllipse(dot, 19, 19, 11, 11);
        }
        IntPtr h = bmp.GetHicon();
        var next = (Drawing.Icon)Drawing.Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        icon.Icon = next;
        current?.Dispose();
        current = next;
        string tip = "Acer Mode: " + (mode?.ToString() ?? "desconhecido");
        icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    void ShowMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var m in ThermalModes.All)
        {
            var item = new MenuItem { Header = m.ToString(), IsChecked = state.ActiveMode == m };
            item.Click += (_, _) => _ = state.SelectModeAsync(m);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var open = new MenuItem { Header = "Abrir Acer Mode" };
        open.Click += (_, _) => openWindow();
        menu.Items.Add(open);
        if (!state.IsDemo)
        {
            var auto = new MenuItem { Header = "Iniciar com o Windows", IsChecked = getAutostart() };
            auto.Click += (_, _) => setAutostart(!auto.IsChecked);
            menu.Items.Add(auto);
        }
        var quit = new MenuItem { Header = "Sair" };
        quit.Click += (_, _) => exit();
        menu.Items.Add(quit);

        SetForegroundWindow(new WindowInteropHelper(menuHost).Handle);
        menu.PlacementTarget = menuHost;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        current?.Dispose();
        baseIcon.Dispose();
        menuHost.Close();
    }
}
