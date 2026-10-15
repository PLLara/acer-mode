namespace AcerMode.Core;

// Tradução pura entre o domínio e os inteiros/bytes da interface WMI AcerGamingFunction.
// Formatos confirmados no PHN16-72: ver spec §3.1.
public static class Encoders
{
    public const uint ThermalSetting = 0x0B;
    public const uint SensorCommand = 0x01;   // 0x02 (outros modelos) devolve zero aqui
    public const uint BothFans = 0x09;        // bitmask CPU (0x01) | GPU (0x08)

    public static bool StatusOk(ulong output) => (output & 0xFF) == 0;

    public static ulong ThermalSet(ThermalMode m) => ((ulong)m << 8) | ThermalSetting;
    public static int ThermalParse(ulong output) => (int)((output >> 8) & 0xFF);

    public static ulong ZoneColor(int zone, Rgb c)
    {
        if (zone is < 0 or >= LightingProfile.ZoneCount) throw new ArgumentOutOfRangeException(nameof(zone));
        return (1UL << zone) | ((ulong)c.R << 8) | ((ulong)c.G << 16) | ((ulong)c.B << 24);
    }

    public static Rgb ZoneColorParse(ulong output) => new((byte)(output >> 8), (byte)(output >> 16), (byte)(output >> 24));

    // [efeito, velocidade, brilho, 0, direção, R, G, B, motor (3 = quatro zonas), 1, 0...]
    public static byte[] Backlight(LightingProfile profile)
    {
        var p = profile.Normalized();
        bool isStatic = p.Effect == KbEffect.Static;
        var c = isStatic ? p.Zones[0] : p.Color;
        return new byte[]
        {
            (byte)p.Effect, (byte)(isStatic ? 0 : p.Speed), (byte)p.Brightness, 0,
            (byte)(isStatic ? 0 : (byte)p.Direction), c.R, c.G, c.B, 3, 1, 0, 0, 0, 0, 0, 0,
        };
    }

    public static uint SensorQuery(Sensor s) => SensorCommand | ((uint)s << 8);
    public static int? SensorParse(ulong output) => StatusOk(output) ? (int)((output >> 8) & 0xFFFF) : null;

    public static ulong FanBehaviorSet(FanMode cpu, FanMode gpu) => BothFans | ((ulong)cpu << 16) | ((ulong)gpu << 22);

    public static (FanMode Cpu, FanMode Gpu)? FanBehaviorParse(ulong output)
    {
        if (!StatusOk(output)) return null;
        ulong d = output >> 8;
        return ((FanMode)(d & 3), (FanMode)((d >> 6) & 3));
    }

    public static ulong FanSpeedSet(Fan fan, int percent) => (ulong)fan | ((ulong)Math.Clamp(percent, 0, 100) << 8);
    public static int? FanSpeedParse(ulong output) => StatusOk(output) ? (int)((output >> 8) & 0xFF) : null;

    // Botão físico de modo: APGeEvent com payload 07 05 01.
    public static bool IsModeKeyEvent(byte[]? detail) => detail is { Length: >= 3 } && detail[0] == 7 && detail[1] == 5 && detail[2] == 1;
}
