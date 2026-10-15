using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Acer Mode: troca o perfil térmico do Acer Predator pela interface WMI da BIOS (sem PredatorSense).

static class Bios
{
    public static readonly string[] Names = { "Quiet", "Balanced", "Performance", "Turbo", "Eco" };
    public static readonly int[] Codes = { 0, 1, 4, 5, 6 };

    static ManagementObject Obj()
    {
        var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
        foreach (ManagementObject o in s.Get()) return o;
        throw new Exception("Interface AcerGamingFunction não encontrada.");
    }

    // WMI às vezes trava (ex.: ao voltar da suspensão); roda numa thread à parte com limite de tempo
    // para a janela nunca congelar. Depois de uma falha, pausa 30 s antes de tentar de novo.
    static int failUntil;
    static T WithTimeout<T>(Func<T> f)
    {
        if (Environment.TickCount - failUntil < 0) throw new Exception("A BIOS não respondeu (nova tentativa em instantes).");
        var t = System.Threading.Tasks.Task.Run(f);
        if (!t.Wait(4000)) { failUntil = Environment.TickCount + 30000; throw new Exception("A BIOS não respondeu."); }
        return t.Result;
    }

    public static int Current() { return WithTimeout<int>(CurrentCore); }
    public static void Set(int code) { WithTimeout<bool>(() => { SetCore(code); return true; }); }

    static int CurrentCore()
    {
        var o = Obj();
        var p = o.GetMethodParameters("GetGamingMiscSetting");
        p["gmInput"] = (uint)0x0B;
        var r = o.InvokeMethod("GetGamingMiscSetting", p, null);
        ulong v = (ulong)r["gmOutput"];
        return (int)((v >> 8) & 0xFF);
    }

    static void SetCore(int code)
    {
        var o = Obj();
        var p = o.GetMethodParameters("SetGamingMiscSetting");
        p["gmInput"] = ((ulong)code << 8) | 0x0B;
        var r = o.InvokeMethod("SetGamingMiscSetting", p, null);
        uint status = (uint)r["gmOutput"];
        if ((status & 0xFF) != 0) throw new Exception("A BIOS recusou a mudança (status " + status + ").");

        try { SetKeyboard(o, code); } catch { }   // a cor do teclado nunca deve impedir a troca de modo
    }

    // Cor do teclado (4 zonas) por modo. Protocolo do ASense para o PHN16-72:
    // SetGamingRgbKb = zona(bitmask) | R<<8 | G<<16 | B<<24; SetGamingKBBacklight = 16 bytes
    // [modo, velocidade, brilho, 0, direção, R, G, B, engine(3 = quatro zonas), 1, 0...]
    static void SetKeyboard(ManagementObject o, int code)
    {
        byte r, g, b, bright;
        switch (code)
        {
            case 5: r = 255; g = 45; b = 10; bright = 100; break;    // Turbo: vermelho-laranja
            case 4: r = 150; g = 70; b = 255; bright = 100; break;   // Performance: roxo
            case 0: r = 0; g = 190; b = 255; bright = 45; break;     // Quiet: ciano, mais fraco
            case 6: r = 40; g = 220; b = 90; bright = 70; break;     // Eco: verde
            default: r = 40; g = 120; b = 255; bright = 100; break;  // Balanced: azul
        }
        var zp = o.GetMethodParameters("SetGamingRgbKb");
        for (int z = 0; z < 4; z++)
        {
            zp["gmInput"] = (1UL << z) | ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24);
            o.InvokeMethod("SetGamingRgbKb", zp, null);
        }
        var ep = o.GetMethodParameters("SetGamingKBBacklight");
        ep["gmInput"] = new byte[] { 0, 0, bright, 0, 0, r, g, b, 3, 1, 0, 0, 0, 0, 0, 0 };
        o.InvokeMethod("SetGamingKBBacklight", ep, null);
    }
}

static class Autostart
{
    const string TaskName = "AcerMode";

    static int Run(string args, out string output)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = Process.Start(psi)) { output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); return p.ExitCode; }
    }

    public static bool IsOn() { string o; return Run("/Query /TN " + TaskName, out o) == 0; }

    public static void Set(bool on)
    {
        string o;
        if (!on) { Run("/Delete /TN " + TaskName + " /F", out o); return; }
        string user = WindowsIdentity.GetCurrent().Name;
        string exe = Application.ExecutablePath;
        // XML próprio: o padrão do schtasks não inicia na bateria, e isto é um notebook.
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
            "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
            "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>--tray</Arguments></Exec></Actions>\n" +
            "</Task>";
        string tmp = Path.Combine(Path.GetTempPath(), "acermode-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        int code = Run("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F", out o);
        File.Delete(tmp);
        if (code != 0) throw new Exception(o.Trim());
    }
}

static class Ease
{
    public static float OutCubic(float x) { x = Clamp(x); float u = 1f - x; return 1f - u * u * u; }
    public static float OutBack(float x) { x = Clamp(x); const float c1 = 1.4f, c3 = c1 + 1f; float u = x - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
    public static float Clamp(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
    public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Clamp(t);
        return Color.FromArgb((int)Lerp(a.A, b.A, t), (int)Lerp(a.R, b.R, t), (int)Lerp(a.G, b.G, t), (int)Lerp(a.B, b.B, t));
    }
}

// Desenha toda a interface (peças geradas pelo Codex + texto) num bitmap com transparência real e animações.
class Skin
{
    public const int W = 368, H = 458;                 // tamanho lógico da janela (px a 96 dpi)

    public int Current = -1, Hover = -1, Pressed = -1; // Hover/Pressed: 0-4 modos, 10 fechar, 11 minimizar, 12 liga/desliga
    public bool StartupOn;
    public string Msg;                                 // erro a mostrar no lugar do status

    // valores animados (0..1) que seguem o estado com suavização
    readonly float[] hoverA = new float[5], pressA = new float[5], activeA = new float[5];
    float closeA, minA, toggleHoverA, toggleOnA, phase;

    // Área visível de cada peça dentro do PNG (os PNGs têm margem/brilho em volta).
    static readonly Rectangle SrcPanel = new Rectangle(24, 24, 1423, 1783);
    static readonly Rectangle SrcTitle = new Rectangle(20, 18, 1433, 139);
    static readonly Rectangle SrcBtn = new Rectangle(18, 18, 1275, 171);
    static readonly Rectangle SrcRound = new Rectangle(18, 18, 141, 139);
    static readonly Rectangle SrcToggle = new Rectangle(16, 30, 239, 99);

    public static RectangleF ModeRect(int i) { return new RectangleF(20, 94 + i * 49, 328, 44); }
    public static readonly RectangleF CloseRect = new RectangleF(330, 5, 26, 26);
    public static readonly RectangleF MinRect = new RectangleF(298, 5, 26, 26);
    public static readonly RectangleF ToggleRow = new RectangleF(20, 406, 328, 38);
    static readonly RectangleF ToggleImg = new RectangleF(302, 415, 46, 19);
    public static readonly RectangleF DragRect = new RectangleF(0, 0, 292, 36);

    readonly Bitmap panel, titlebar, idle, active, turbo, close, min, tOff, tOn;
    public readonly Bitmap Icon;
    Bitmap staticLayer; float staticScale;
    static readonly Font fTitle = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font fHead = new Font("Segoe UI Semibold", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font fBtn = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font fText = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font fBold = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Pixel);

    static Bitmap[] modeIcons;
    public static Bitmap ModeIcon(int idx)
    {
        if (modeIcons == null)
            modeIcons = new Bitmap[] { Load("mode-quiet.png"), Load("mode-balanced.png"), Load("mode-performance.png"), Load("mode-turbo.png"), Load("mode-eco.png") };
        return modeIcons[idx];
    }

    // Ícones de modo (brancos, gerados pelo Codex) pintados na cor pedida.
    public static void DrawModeIcon(Graphics g, int idx, float cx, float cy, float size, Color tint)
    {
        var cm = new ColorMatrix(new float[][] {
            new float[] {tint.R / 255f, 0, 0, 0, 0}, new float[] {0, tint.G / 255f, 0, 0, 0},
            new float[] {0, 0, tint.B / 255f, 0, 0}, new float[] {0, 0, 0, tint.A / 255f, 0}, new float[] {0, 0, 0, 0, 1} });
        var img = ModeIcon(idx);
        var pts = new PointF[] { new PointF(cx - size / 2, cy - size / 2), new PointF(cx + size / 2, cy - size / 2), new PointF(cx - size / 2, cy + size / 2) };
        using (var ia = new ImageAttributes())
        {
            ia.SetColorMatrix(cm);
            g.DrawImage(img, pts, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
        }
    }

    public static Bitmap Load(string name)
    {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms); ms.Position = 0;
            using (var tmp = new Bitmap(ms)) return new Bitmap(tmp);
        }
    }

    public Skin()
    {
        panel = Load("panel.png"); titlebar = Load("titlebar.png"); idle = Load("btn-idle.png");
        active = Load("btn-active.png"); turbo = Load("btn-turbo.png"); close = Load("btn-close.png");
        min = Load("btn-min.png"); tOff = Load("toggle-off.png"); tOn = Load("toggle-on.png"); Icon = Load("icon.png");
    }

    // ---- animação ----
    static float Approach(float v, float target, float k, float dt)
    {
        float nv = v + (target - v) * (1f - (float)Math.Exp(-k * dt));
        return Math.Abs(nv - target) < 0.004f ? target : nv;
    }

    float TargetActive(int i) { return Bios.Codes[i] == Current ? 1f : 0f; }

    // Pula direto para o estado atual, sem transição (primeira exibição).
    public void Snap()
    {
        for (int i = 0; i < 5; i++) { hoverA[i] = Hover == i ? 1f : 0f; pressA[i] = Pressed == i ? 1f : 0f; activeA[i] = TargetActive(i); }
        closeA = Hover == 10 ? 1f : 0f; minA = Hover == 11 ? 1f : 0f; toggleHoverA = Hover == 12 ? 1f : 0f; toggleOnA = StartupOn ? 1f : 0f;
    }

    // Avança as animações; devolve true enquanto ainda estiver se movendo.
    public bool Update(float dt)
    {
        bool moving = false;
        for (int i = 0; i < 5; i++)
        {
            float th = Hover == i ? 1f : 0f, tp = Pressed == i ? 1f : 0f, ta = TargetActive(i);
            hoverA[i] = Approach(hoverA[i], th, 13f, dt);
            pressA[i] = Approach(pressA[i], tp, 38f, dt);
            activeA[i] = Approach(activeA[i], ta, 10f, dt);
            if (hoverA[i] != th || pressA[i] != tp || activeA[i] != ta) moving = true;
        }
        float tc = Hover == 10 ? 1f : 0f, tm = Hover == 11 ? 1f : 0f, tt = Hover == 12 ? 1f : 0f, to = StartupOn ? 1f : 0f;
        closeA = Approach(closeA, tc, 14f, dt); minA = Approach(minA, tm, 14f, dt);
        toggleHoverA = Approach(toggleHoverA, tt, 14f, dt); toggleOnA = Approach(toggleOnA, to, 12f, dt);
        if (closeA != tc || minA != tm || toggleHoverA != tt || toggleOnA != to) moving = true;
        phase += dt * 3.2f; if (phase > 6.2832f) phase -= 6.2832f;
        return moving;
    }

    public bool Pulsing { get { return Current == 5; } }

    // ---- desenho ----
    static void Draw(Graphics g, Bitmap img, Rectangle src, RectangleF dst, float alpha)
    {
        if (alpha <= 0.004f) return;
        var pts = new PointF[] { new PointF(dst.Left, dst.Top), new PointF(dst.Right, dst.Top), new PointF(dst.Left, dst.Bottom) };
        if (alpha >= 0.996f) { g.DrawImage(img, pts, src, GraphicsUnit.Pixel); return; }
        var cm = new ColorMatrix(new float[][] {
            new float[] {1,0,0,0,0}, new float[] {0,1,0,0,0}, new float[] {0,0,1,0,0}, new float[] {0,0,0,alpha,0}, new float[] {0,0,0,0,1} });
        using (var ia = new ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(img, pts, src, GraphicsUnit.Pixel, ia); }
    }

    static RectangleF Shrink(RectangleF r, float d) { return new RectangleF(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d); }

    static void Quality(Graphics g, float s)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(s, s);
    }

    // O que nunca muda (painel, barra de título, textos fixos) é desenhado uma vez só por escala.
    void EnsureStatic(float s)
    {
        if (staticLayer != null && staticScale == s) return;
        if (staticLayer != null) staticLayer.Dispose();
        staticScale = s;
        staticLayer = new Bitmap((int)Math.Round(W * s), (int)Math.Round(H * s), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(staticLayer))
        {
            g.Clear(Color.Transparent);
            Quality(g, s);
            Draw(g, panel, SrcPanel, new RectangleF(0, 0, W, H), 1f);
            Draw(g, titlebar, SrcTitle, new RectangleF(0, 0, W, 36), 1f);
            g.DrawImage(Icon, new RectangleF(11, 5, 26, 26), new RectangleF(150, 150, 724, 724), GraphicsUnit.Pixel);
            using (var b = new SolidBrush(Color.FromArgb(235, 240, 250))) g.DrawString("Acer Mode", fTitle, b, 44, 12);
            using (var b = new SolidBrush(Color.White)) g.DrawString("Modo do notebook", fHead, b, 19, 46);
            using (var p = new Pen(Color.FromArgb(34, 255, 255, 255), 1f)) { g.DrawLine(p, 20, 352, 348, 352); g.DrawLine(p, 20, 398, 348, 398); }
            using (var b = new SolidBrush(Color.FromArgb(222, 228, 240))) g.DrawString("Iniciar com o Windows", fText, b, 20, 417);
        }
    }

    public Bitmap Render(float s)
    {
        EnsureStatic(s);
        int pw = (int)Math.Round(W * s), ph = (int)Math.Round(H * s);
        var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.DrawImageUnscaled(staticLayer, 0, 0);
            Quality(g, s);

            Draw(g, min, SrcRound, MinRect, 0.7f + 0.3f * minA);
            Draw(g, close, SrcRound, CloseRect, 0.7f + 0.3f * closeA);

            var idleText = Color.FromArgb(225, 230, 240);
            var idleIcon = Color.FromArgb(150, 165, 192);
            for (int i = 0; i < Bios.Names.Length; i++)
            {
                bool isTurbo = Bios.Codes[i] == 5;
                float a = activeA[i], hv = hoverA[i], pr = pressA[i];
                var r = Shrink(ModeRect(i), 1.8f * pr);                      // afunda ao clicar
                Draw(g, idle, SrcBtn, r, 1f);
                Draw(g, active, SrcBtn, r, 0.32f * hv * (1f - a));           // brilho do hover
                float pulse = isTurbo ? 0.84f + 0.16f * (0.5f + 0.5f * (float)Math.Sin(phase)) : 1f;
                Draw(g, isTurbo ? turbo : active, SrcBtn, r, a * pulse);     // seleção entra/sai com cross-fade

                Color ic = isTurbo ? Ease.Lerp(Color.FromArgb(255, 98, 52), Color.FromArgb(255, 150, 110), a) : Ease.Lerp(idleIcon, Color.White, a);
                DrawModeIcon(g, i, r.Left + 28, r.Top + r.Height / 2, 28f + 2f * hv, ic);
                using (var b = new SolidBrush(Ease.Lerp(idleText, Color.White, Math.Max(a, hv * 0.6f))))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(Bios.Names[i], fBtn, b, new RectangleF(r.Left, r.Top, r.Width, r.Height), sf);
            }

            var gray = Color.FromArgb(160, 170, 188);
            if (Msg != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(255, 120, 110))) g.DrawString(Msg, fText, b, new RectangleF(20, 366, 328, 30));
            }
            else
            {
                int at = Array.IndexOf(Bios.Codes, Current);
                string lead = "Ativo agora:";
                string name = at >= 0 ? Bios.Names[at] : "—";
                var sf = StringFormat.GenericTypographic;
                float lw = g.MeasureString(lead, fText, 1000, sf).Width;
                using (var b = new SolidBrush(gray)) g.DrawString(lead, fText, b, 20, 368, sf);
                using (var b = new SolidBrush(Current == 5 ? Color.FromArgb(255, 110, 70) : Color.FromArgb(77, 163, 255)))
                    g.DrawString(name, fBold, b, 20 + lw + 7, 367, sf);
            }

            Draw(g, tOff, SrcToggle, ToggleImg, (0.92f + 0.08f * toggleHoverA));
            Draw(g, tOn, SrcToggle, ToggleImg, toggleOnA * (0.92f + 0.08f * toggleHoverA));
        }
        return bmp;
    }
}

// Envia um bitmap com alpha por pixel para uma janela em camadas.
static class Layered
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")] static extern bool ULW(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")] static extern bool ULWPos(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    // pos == null mantém a posição atual da janela.
    public static void Push(IntPtr hwnd, Bitmap bmp, byte alpha, Point? pos)
    {
        int w = bmp.Width, h = bmp.Height;
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            IntPtr bits;
            hBmp = CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            var buf = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(data);
            Marshal.Copy(buf, 0, bits, buf.Length);
            old = SelectObject(memDc, hBmp);
            var size = new SIZE { cx = w, cy = h };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            if (pos.HasValue)
            {
                var dst = new POINT { x = pos.Value.X, y = pos.Value.Y };
                ULWPos(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
            }
            else ULW(hwnd, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memDc, old);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

// Aviso rápido na tela com o modo novo: entra com "pop", não rouba o foco, não bloqueia cliques, some sozinho.
class Osd : Form
{
    const int OW = 280, OH = 76;
    readonly float S;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    int shownAt, mode = 1, baseX, baseY;
    bool steadyDrawn;
    static readonly Font fCap = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly Font fName = new Font("Segoe UI Semibold", 28f, FontStyle.Regular, GraphicsUnit.Pixel);
    static Bitmap pill, pillTurbo;
    static Bitmap Pill() { if (pill == null) pill = Skin.Load("osd-pill.png"); return pill; }
    static Bitmap PillTurbo() { if (pillTurbo == null) pillTurbo = Skin.Load("osd-pill-turbo.png"); return pillTurbo; }

    public Osd(float scale)
    {
        S = scale;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size((int)Math.Round(OW * S), (int)Math.Round(OH * S));
        timer.Interval = 15; timer.Tick += (s, e) => Tick();
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        // em camadas + transparente ao mouse + sem ativar + fora do Alt-Tab
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return cp; }
    }

    public static Bitmap Render(int code, float s) { return Render(code, s, 1f); }

    // f = escala do conteúdo (para o efeito de "pop")
    public static Bitmap Render(int code, float s, float f)
    {
        int idx = Array.IndexOf(Bios.Codes, code); if (idx < 0) idx = 1;
        bool turbo = code == 5;
        var bmp = new Bitmap((int)Math.Round(OW * s), (int)Math.Round(OH * s), PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(s, s);
            g.TranslateTransform(OW / 2f, OH / 2f); g.ScaleTransform(f, f); g.TranslateTransform(-OW / 2f, -OH / 2f);

            var pillImg = turbo ? PillTurbo() : Pill();   // fundo e ícones gerados pelo Codex, no mesmo estilo da janela
            g.DrawImage(pillImg, new RectangleF(0, 0, OW, OH), new RectangleF(0, 0, pillImg.Width, pillImg.Height), GraphicsUnit.Pixel);
            Skin.DrawModeIcon(g, idx, 52, OH / 2f, 52f, turbo ? Color.FromArgb(255, 105, 62) : Color.White);
            using (var cap = new SolidBrush(Color.FromArgb(160, 170, 188))) g.DrawString("MODO ATIVO", fCap, cap, 82, 17);
            using (var nm = new SolidBrush(Color.White)) g.DrawString(Bios.Names[idx], fName, nm, 80, 30);
        }
        return bmp;
    }

    public void Flash(int code)
    {
        mode = code;
        bool alive = Visible && timer.Enabled && (Environment.TickCount - shownAt) < 1400;
        if (!alive)
        {
            var scr = Screen.FromPoint(Cursor.Position).WorkingArea;   // aparece onde você está olhando
            baseX = scr.Left + (scr.Width - ClientSize.Width) / 2;
            baseY = scr.Bottom - ClientSize.Height - (int)(70 * S);
            Left = baseX; Top = baseY;
            if (!Visible) Show();
            shownAt = Environment.TickCount;
        }
        else shownAt = Environment.TickCount - 180;   // já visível: troca o modo sem repetir a entrada
        steadyDrawn = false;
        timer.Start();
        Tick();
    }

    void Tick()
    {
        int t = Environment.TickCount - shownAt;
        float alpha, f, dy;
        if (t < 180) { float x = t / 180f; alpha = Ease.OutCubic(x); f = 0.86f + 0.14f * Ease.OutBack(x); dy = (1f - Ease.OutCubic(x)) * 14f; steadyDrawn = false; }
        else if (t < 1400) { if (steadyDrawn) return; alpha = 1f; f = 1f; dy = 0f; steadyDrawn = true; }
        else if (t < 1750) { float x = (t - 1400) / 350f; alpha = 1f - x; f = 1f - 0.04f * x; dy = 8f * x; steadyDrawn = false; }
        else { timer.Stop(); Hide(); return; }
        using (var bmp = Render(mode, S, f))
            Layered.Push(Handle, bmp, (byte)(255 * Ease.Clamp(alpha)), new Point(baseX, baseY + (int)(dy * S)));
    }
}

// Gera tiras de quadros das animações usando a mesma lógica do app, só para conferir o movimento.
static class AnimShots
{
    static readonly Color Bg = Color.FromArgb(52, 54, 62);

    static Bitmap Over(Bitmap src, float alpha, float dy, int w, int h)
    {
        var c = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(c))
        {
            g.Clear(Bg);
            var cm = new ColorMatrix(new float[][] { new float[] {1,0,0,0,0}, new float[] {0,1,0,0,0}, new float[] {0,0,1,0,0}, new float[] {0,0,0,alpha,0}, new float[] {0,0,0,0,1} });
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(src, new Rectangle((w - src.Width) / 2, (int)((h - src.Height) / 2 + dy), src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
        }
        return c;
    }

    static void Strip(List<Bitmap> frames, List<string> labels, string path)
    {
        int fw = frames[0].Width, fh = frames[0].Height;
        using (var m = new Bitmap(fw * frames.Count, fh + 22, PixelFormat.Format32bppPArgb))
        using (var g = Graphics.FromImage(m))
        using (var f = new Font("Segoe UI", 11f))
        {
            g.Clear(Color.Black);
            for (int i = 0; i < frames.Count; i++) { g.DrawImage(frames[i], i * fw, 22); g.DrawString(labels[i], f, Brushes.White, i * fw + 4, 2); frames[i].Dispose(); }
            m.Save(path, ImageFormat.Png);
        }
    }

    // Avança a animação em passos curtos e guarda um quadro em cada instante pedido.
    static void Sim(Skin sk, float[] times, List<Bitmap> frames, List<string> labels, Action<float> perStep)
    {
        float t = 0f; int next = 0; const float dt = 0.004f;
        while (next < times.Length)
        {
            if (t + 1e-5f >= times[next]) { using (var r = sk.Render(1f)) frames.Add(Over(r, 1f, 0, r.Width + 20, r.Height + 20)); labels.Add("+" + (int)Math.Round(times[next] * 1000) + "ms"); next++; continue; }
            if (perStep != null) perStep(t);
            sk.Update(dt); t += dt;
        }
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        // 1) hover em Performance
        var sk = new Skin(); sk.Current = 1; sk.Snap(); sk.Hover = 2;
        var fr = new List<Bitmap>(); var lb = new List<string>();
        Sim(sk, new float[] { 0f, 0.04f, 0.09f, 0.2f, 0.45f }, fr, lb, null); Strip(fr, lb, Path.Combine(dir, "hover.png"));

        // 2) troca de modo Balanced -> Turbo (cross-fade + pulso)
        sk = new Skin(); sk.Current = 1; sk.Snap(); sk.Hover = 3; sk.Update(1f); sk.Current = 5;
        fr = new List<Bitmap>(); lb = new List<string>();
        Sim(sk, new float[] { 0f, 0.06f, 0.12f, 0.25f, 0.6f }, fr, lb, null); Strip(fr, lb, Path.Combine(dir, "troca.png"));

        // 3) clique afundando o botão
        sk = new Skin(); sk.Current = 1; sk.Snap(); sk.Hover = 3; sk.Update(1f); sk.Pressed = 3;
        fr = new List<Bitmap>(); lb = new List<string>();
        Sim(sk, new float[] { 0f, 0.03f, 0.08f, 0.2f }, fr, lb, null); Strip(fr, lb, Path.Combine(dir, "clique.png"));

        // 4) entrada da janela: fade + subida
        sk = new Skin(); sk.Current = 1; sk.Snap();
        fr = new List<Bitmap>(); lb = new List<string>();
        using (var win = sk.Render(1f))
            foreach (float a in new float[] { 0.05f, 0.2f, 0.4f, 0.7f, 1f })
            { float e = Ease.OutCubic(a); fr.Add(Over(win, e, (1f - e) * 14f, win.Width + 20, win.Height + 40)); lb.Add("showA " + a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)); }
        Strip(fr, lb, Path.Combine(dir, "entrada.png"));

        // 5) aviso na tela: pop-in, estável e fade-out
        fr = new List<Bitmap>(); lb = new List<string>();
        foreach (int t in new int[] { 10, 50, 100, 180, 800, 1500, 1680 })
        {
            float alpha, f, dy;
            if (t < 180) { float x = t / 180f; alpha = Ease.OutCubic(x); f = 0.86f + 0.14f * Ease.OutBack(x); dy = (1f - Ease.OutCubic(x)) * 14f; }
            else if (t < 1400) { alpha = 1f; f = 1f; dy = 0f; }
            else { float x = (t - 1400) / 350f; alpha = 1f - x; f = 1f - 0.04f * x; dy = 8f * x; }
            using (var o = Osd.Render(5, 1f, f)) fr.Add(Over(o, alpha, dy, o.Width + 20, o.Height + 40));
            lb.Add("osd " + t + "ms");
        }
        Strip(fr, lb, Path.Combine(dir, "osd.png"));
    }
}

class MainForm : Form
{
    readonly Skin skin = new Skin();
    Osd osd;
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly float S;
    bool reallyExit, allowShow, hiding;
    float showA = 1f;                       // 0 = invisível, 1 = totalmente visível (entrada/saída da janela)
    long lastTick;
    int baseX, baseY;
    int currentCode = -1;
    ManagementEventWatcher keyWatcher;
    int lastKeyTick, beforeTurbo = 1;
    Icon trayIcon;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    public MainForm(bool startHidden)
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        Text = "Acer Mode";
        ClientSize = new Size((int)Math.Round(Skin.W * S), (int)Math.Round(Skin.H * S));
        Icon = MakeIcon(skin.Icon, Color.Transparent, false);
        allowShow = !startHidden;
        osd = new Osd(S);

        anim.Interval = 15;
        anim.Tick += (s, e) => AnimTick();

        tray.Visible = true;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Opening += (s, e) => BuildMenu();

        poll.Interval = 10000;
        poll.Tick += (s, e) => RefreshState();
        poll.Start();

        // Botão físico de modo: a BIOS dispara APGeEvent com payload 07 05 01 a cada aperto.
        var h = Handle;   // garante o handle para o BeginInvoke, mesmo iniciando escondido
        try
        {
            keyWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new WqlEventQuery("SELECT * FROM APGeEvent"));
            keyWatcher.EventArrived += (s, e) =>
            {
                var d = e.NewEvent["EventDetail"] as byte[];
                KeyLog("evento APGeEvent: " + (d == null ? "(vazio)" : BitConverter.ToString(d)));
                if (d != null && d.Length >= 3 && d[0] == 7 && d[1] == 5 && d[2] == 1)
                    BeginInvoke(new Action(OnHardwareKey));
            };
            keyWatcher.Stopped += (s, e) => KeyLog("watcher parou: " + e.Status);
            keyWatcher.Start();
            KeyLog("watcher iniciado (escutando APGeEvent)");
        }
        catch (Exception ex) { KeyLog("watcher FALHOU: " + ex.Message); skin.Msg = "Botão físico indisponível"; }

        try { skin.StartupOn = Autostart.IsOn(); } catch { }
        RefreshState();
        skin.Snap();
    }

    // Janela em camadas (transparência por pixel) e iniciável escondida na bandeja.
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000; cp.Style |= 0x00020000; return cp; }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow) { if (!IsHandleCreated) CreateHandle(); value = false; }
        base.SetVisibleCore(value);
    }

    // Ao aparecer: começa invisível e anima a entrada (fade + subida).
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { showA = 0f; hiding = false; baseX = Left; baseY = Top; skin.Snap(); StartAnim(); }
        else anim.Stop();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) HideAnimated(); base.OnKeyDown(e); }

    // Fechar (X) só esconde na bandeja; "Sair" no menu do ícone encerra de verdade.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!reallyExit && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; HideAnimated(); }
        else { tray.Visible = false; if (keyWatcher != null) keyWatcher.Stop(); }
        base.OnFormClosing(e);
    }

    // ---- animação ----
    void HideAnimated() { if (!Visible) return; hiding = true; StartAnim(); }

    void StartAnim() { if (!IsHandleCreated || !Visible) return; if (!anim.Enabled) { lastTick = clock.ElapsedMilliseconds; anim.Start(); } }

    void Redraw() { StartAnim(); }

    void AnimTick()
    {
        long now = clock.ElapsedMilliseconds;
        float dt = Math.Min(0.05f, (now - lastTick) / 1000f); lastTick = now;
        bool moving = skin.Update(dt);

        float target = hiding ? 0f : 1f;
        if (showA != target)
        {
            showA += Math.Sign(target - showA) * dt / (hiding ? 0.16f : 0.24f);
            if ((target > showA) == (target < 0.5f)) showA = target;   // passou do alvo
            showA = Ease.Clamp(showA);
            moving = true;
        }
        if (hiding && showA <= 0f)
        {
            hiding = false; anim.Stop();
            Location = new Point(baseX, baseY);   // volta à posição certa antes de esconder
            Hide();
            return;
        }

        using (var bmp = skin.Render(S))
        {
            if (showA >= 1f) Layered.Push(Handle, bmp, 255, null);
            else
            {
                float e = Ease.OutCubic(showA);
                Layered.Push(Handle, bmp, (byte)(255 * e), new Point(baseX, baseY + (int)((1f - e) * 14f * S)));
            }
        }
        if (!moving && !skin.Pulsing) anim.Stop();
    }

    // ---- mouse ----
    int HitTest(Point px)
    {
        var p = new PointF(px.X / S, px.Y / S);
        for (int i = 0; i < Bios.Names.Length; i++) if (Skin.ModeRect(i).Contains(p)) return i;
        if (Skin.CloseRect.Contains(p)) return 10;
        if (Skin.MinRect.Contains(p)) return 11;
        if (Skin.ToggleRow.Contains(p)) return 12;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != skin.Hover) { skin.Hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Redraw(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (skin.Hover != -1 || skin.Pressed != -1) { skin.Hover = -1; skin.Pressed = -1; Redraw(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int h = HitTest(e.Location);
            if (h >= 0) { skin.Pressed = h; Redraw(); }
            else if (Skin.DragRect.Contains(new PointF(e.X / S, e.Y / S))) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int h = HitTest(e.Location), pressed = skin.Pressed;
        skin.Pressed = -1;
        if (e.Button == MouseButtons.Left && h >= 0 && h == pressed) Activate(h);
        Redraw();
        base.OnMouseUp(e);
    }

    void Activate(int idx)
    {
        if (idx < Bios.Names.Length) Apply(Bios.Codes[idx]);
        else if (idx == 10 || idx == 11) HideAnimated();
        else if (idx == 12) ToggleStartup();
    }

    // ---- ações ----
    public void ShowWindow()
    {
        if (InvokeRequired) { BeginInvoke(new Action(ShowWindow)); return; }
        if (Visible) { hiding = false; StartAnim(); WindowState = FormWindowState.Normal; base.Activate(); BringToFront(); }
        else { allowShow = true; Show(); base.Activate(); }
        RefreshState();
    }

    public void TestFlash() { osd.Flash(5); }

    void ExitApp() { reallyExit = true; tray.Visible = false; if (keyWatcher != null) keyWatcher.Stop(); Application.Exit(); }

    void BuildMenu()
    {
        var m = tray.ContextMenuStrip; m.Items.Clear();
        RefreshState();
        for (int i = 0; i < Bios.Names.Length; i++)
        {
            int code = Bios.Codes[i];
            var it = new ToolStripMenuItem(Bios.Names[i]) { Checked = code == currentCode };
            it.Click += (s, e) => Apply(code);
            m.Items.Add(it);
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Abrir janela", null, (s, e) => ShowWindow());
        var st = new ToolStripMenuItem("Iniciar com o Windows") { Checked = skin.StartupOn };
        st.Click += (s, e) => ToggleStartup();
        m.Items.Add(st);
        m.Items.Add("Sair", null, (s, e) => ExitApp());
    }

    // Aperto do botão físico: liga o Turbo; apertar de novo volta ao modo que estava antes.
    void OnHardwareKey()
    {
        int now = Environment.TickCount;
        if (now - lastKeyTick < 600) { KeyLog("  ignorado (repetido)"); return; }   // a BIOS às vezes dispara o mesmo aperto duas vezes
        lastKeyTick = now;
        try
        {
            int cur = Bios.Current();
            if (cur == 5) Bios.Set(beforeTurbo);
            else { beforeTurbo = cur; Bios.Set(5); }
            int now2 = Bios.Current();
            KeyLog("  modo " + cur + " -> " + now2);
            RefreshState();
            osd.Flash(now2);
        }
        catch (Exception ex) { KeyLog("  ERRO: " + ex.Message); skin.Msg = "Erro: " + ex.Message; Redraw(); }
    }

    static void KeyLog(string msg)
    {
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "key.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n"); } catch { }
    }

    void Apply(int code)
    {
        try { Bios.Set(code); RefreshState(); osd.Flash(code); }
        catch (Exception ex) { skin.Msg = "Erro: " + ex.Message; Redraw(); }
    }

    void ToggleStartup()
    {
        try { Autostart.Set(!skin.StartupOn); skin.StartupOn = !skin.StartupOn; skin.Msg = null; }
        catch (Exception ex) { skin.Msg = "Erro: " + ex.Message; }
        Redraw();
    }

    void RefreshState()
    {
        try
        {
            int cur = Bios.Current();
            currentCode = cur; skin.Current = cur; skin.Msg = null;
            int at = Array.IndexOf(Bios.Codes, cur);
            string name = at >= 0 ? Bios.Names[at] : "desconhecido (" + cur + ")";
            SetTrayIcon(cur == 5 ? Color.FromArgb(255, 80, 40) : Color.FromArgb(77, 163, 255), "Acer Mode: " + name);
        }
        catch (Exception ex) { skin.Msg = "Erro: " + ex.Message; }
        Redraw();
    }

    void SetTrayIcon(Color badge, string tip)
    {
        var old = trayIcon;
        trayIcon = MakeIcon(skin.Icon, badge, true);
        tray.Icon = trayIcon;
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        if (old != null) old.Dispose();
    }

    // Ícone do app; na bandeja ganha uma bolinha na cor do modo (vermelho = Turbo).
    static Icon MakeIcon(Bitmap src, Color badge, bool withBadge)
    {
        using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, 32, 32), new Rectangle(150, 150, 724, 724), GraphicsUnit.Pixel);
                if (withBadge)
                {
                    using (var ring = new SolidBrush(Color.FromArgb(24, 24, 27))) g.FillEllipse(ring, 17, 17, 15, 15);
                    using (var dot = new SolidBrush(badge)) g.FillEllipse(dot, 19, 19, 11, 11);
                }
            }
            IntPtr h = bmp.GetHicon();
            Icon tmp = Icon.FromHandle(h);
            Icon copy = (Icon)tmp.Clone();
            tmp.Dispose(); DestroyIcon(h);
            return copy;
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        // Modo de teste sem janela: AcerMode.exe --set turbo | --status
        if (args.Length > 0 && (args[0] == "--set" || args[0] == "--status"))
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-status.txt");
            try
            {
                if (args[0] == "--set" && args.Length > 1)
                {
                    int i = Array.FindIndex(Bios.Names, n => n.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                    if (i < 0) { File.WriteAllText(file, "ERR modo inválido"); return 2; }
                    Bios.Set(Bios.Codes[i]);
                }
                int cur = Bios.Current(); int at = Array.IndexOf(Bios.Codes, cur);
                File.WriteAllText(file, "Profile: " + (at >= 0 ? Bios.Names[at] : "desconhecido (" + cur + ")"));
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(file, "ERR " + ex.Message); return 1; }
        }

        // Imagem da interface sem abrir janela: AcerMode.exe --shot arquivo.png [escala]
        if (args.Length > 1 && args[0] == "--shot")
        {
            float sc = 2f; if (args.Length > 2) float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc);
            var sk = new Skin(); sk.Current = 1; sk.StartupOn = false; sk.Snap();
            using (var b = sk.Render(sc)) b.Save(args[1], ImageFormat.Png);
            return 0;
        }

        // Imagem do aviso de modo: AcerMode.exe --shot-osd arquivo.png codigo [fator]
        if (args.Length > 2 && args[0] == "--shot-osd")
        {
            float f = 1f; if (args.Length > 3) float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f);
            using (var b = Osd.Render(int.Parse(args[2]), 2f, f)) b.Save(args[1], ImageFormat.Png);
            return 0;
        }

        // Quadros das animações desenhados fora da tela (não captura a tela do usuário): AcerMode.exe --shot-anim pasta
        if (args.Length > 1 && args[0] == "--shot-anim") { AnimShots.Run(args[1]); return 0; }

        // Teste visual das animações (sem instância única, janela por cima): AcerMode.exe --test
        if (Array.IndexOf(args, "--test") >= 0)
        {
            Application.EnableVisualStyles();
            var tf = new MainForm(false); tf.TopMost = true;
            var tm = new System.Windows.Forms.Timer { Interval = 2500 };
            tm.Tick += (s, e) => { tm.Stop(); tf.TestFlash(); };
            tm.Start();
            Application.Run(tf);
            return 0;
        }

        // Uma instância só: abrir de novo apenas traz a janela da instância que já está na bandeja.
        bool created;
        var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AcerModeShow");
        using (var mutex = new Mutex(true, @"Local\AcerModeSingle", out created))
        {
            if (!created) { showEvt.Set(); return 0; }
            bool hidden = Array.IndexOf(args, "--tray") >= 0;
            Application.EnableVisualStyles();
            var form = new MainForm(hidden);
            var t = new Thread(() => { while (true) { showEvt.WaitOne(); form.ShowWindow(); } }) { IsBackground = true };
            t.Start();
            Application.Run(form);
        }
        return 0;
    }
}
