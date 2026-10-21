using System.Windows;

namespace AcerMode.App.Controls;

// Mapa de teclas do PHN16-72 desenhado em unidades de tecla (1u = largura de uma letra).
// Cada tecla cai numa das 4 zonas RGB pela posição horizontal do centro, que é como o
// teclado de 4 zonas divide a iluminação. Legendas em duas linhas separadas por "·": símbolo em cima, base embaixo.
public sealed record KeyCap(Rect Bounds, string Top, string Bottom, int Zone);

public static class KeyboardLayout
{
    public const double Width = 15, Height = 5.75;   // linha de função mais baixa (0,75u)
    public const int Zones = 4;

    public static readonly IReadOnlyList<KeyCap> Keys = Build();

    static List<KeyCap> Build()
    {
        var keys = new List<KeyCap>();
        double y = 0;

        void Row(double height, params (string Label, double W)[] row)
        {
            double x = 0;
            foreach (var (label, w) in row)
            {
                var parts = label.Split('·');
                string top = parts.Length > 1 ? parts[0] : "", bottom = parts.Length > 1 ? parts[1] : parts[0];
                var r = new Rect(x, y, w, height);
                keys.Add(new KeyCap(r, top, bottom, ZoneOf(r)));
                x += w;
            }
            y += height;
        }

        const double fk = 15.0 / 16;
        Row(0.75, ("Esc", fk), ("F1", fk), ("F2", fk), ("F3", fk), ("F4", fk), ("F5", fk), ("F6", fk), ("F7", fk), ("F8", fk),
            ("F9", fk), ("F10", fk), ("F11", fk), ("F12", fk), ("PrtSc", fk), ("Ins", fk), ("Del", fk));
        Row(1, ("~·`", 1), ("!·1", 1), ("@·2", 1), ("#·3", 1), ("$·4", 1), ("%·5", 1), ("^·6", 1), ("&·7", 1), ("*·8", 1),
            ("(·9", 1), (")·0", 1), ("_·-", 1), ("+·=", 1), ("←", 2));
        Row(1, ("Tab", 1.5), ("Q", 1), ("W", 1), ("E", 1), ("R", 1), ("T", 1), ("Y", 1), ("U", 1), ("I", 1), ("O", 1), ("P", 1),
            ("{·[", 1), ("}·]", 1), ("|·\\", 1.5));
        Row(1, ("Caps", 1.75), ("A", 1), ("S", 1), ("D", 1), ("F", 1), ("G", 1), ("H", 1), ("J", 1), ("K", 1), ("L", 1),
            (":·;", 1), ("\"·'", 1), ("Enter", 2.25));
        Row(1, ("Shift", 2.25), ("Z", 1), ("X", 1), ("C", 1), ("V", 1), ("B", 1), ("N", 1), ("M", 1), ("<·,", 1), (">·.", 1),
            ("?·/", 1), ("Shift", 2.75));

        // última linha: modificadores, espaço e o bloco de setas (↑ e ↓ dividem a altura)
        double bx = 0;
        foreach (var (label, w) in new[] { ("Ctrl", 1.25), ("Fn", 1.0), ("⊞", 1.0), ("Alt", 1.25), ("", 5.0), ("Alt", 1.0), ("≡", 1.0), ("Ctrl", 1.25) })
        {
            var r = new Rect(bx, y, w, 1);
            keys.Add(new KeyCap(r, "", label, ZoneOf(r)));
            bx += w;
        }
        const double aw = 0.75;
        keys.Add(Key(new Rect(bx, y + 0.5, aw, 0.5), "‹"));
        keys.Add(Key(new Rect(bx + aw, y, aw, 0.5), "˄"));
        keys.Add(Key(new Rect(bx + aw, y + 0.5, aw, 0.5), "˅"));
        keys.Add(Key(new Rect(bx + 2 * aw, y + 0.5, aw, 0.5), "›"));
        return keys;

        static KeyCap Key(Rect r, string label) => new(r, "", label, ZoneOf(r));
    }

    static int ZoneOf(Rect r) => Math.Clamp((int)((r.X + r.Width / 2) / Width * Zones), 0, Zones - 1);
}
