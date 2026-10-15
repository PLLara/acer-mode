namespace AcerMode.Core;

public enum KbEffect : byte { Static = 0, Breathing = 1, Neon = 2, Wave = 3, Shifting = 4, Zoom = 5, Meteor = 6, Twinkling = 7 }
public enum KbDirection : byte { RightToLeft = 1, LeftToRight = 2 }

public static class KbEffects
{
    public static readonly KbEffect[] All = Enum.GetValues<KbEffect>();

    // Efeitos que a BIOS do PHN16-72 aceitou na verificação de escrita (spec §3.1).
    public static readonly KbEffect[] Supported = All;
    public static bool UsesZones(KbEffect e) => e == KbEffect.Static;
    public static bool UsesColor(KbEffect e) => e is KbEffect.Breathing or KbEffect.Shifting or KbEffect.Zoom or KbEffect.Meteor or KbEffect.Twinkling;
    public static bool UsesSpeed(KbEffect e) => e != KbEffect.Static;
    public static bool UsesDirection(KbEffect e) => e is KbEffect.Wave or KbEffect.Shifting;

    public static string Label(KbEffect e) => e switch
    {
        KbEffect.Static => "Estático", KbEffect.Breathing => "Respirar", KbEffect.Neon => "Neon",
        KbEffect.Wave => "Onda", KbEffect.Shifting => "Alternar", KbEffect.Zoom => "Zoom",
        KbEffect.Meteor => "Meteoro", KbEffect.Twinkling => "Cintilar", _ => e.ToString(),
    };
}

public sealed record LightingProfile
{
    public const int ZoneCount = 4, MaxSpeed = 9, MaxBrightness = 100;

    public KbEffect Effect { get; init; } = KbEffect.Static;
    public Rgb[] Zones { get; init; } = { new(255, 255, 255), new(255, 255, 255), new(255, 255, 255), new(255, 255, 255) };
    public Rgb Color { get; init; } = new(255, 255, 255);
    public int Brightness { get; init; } = MaxBrightness;
    public int Speed { get; init; } = 5;
    public KbDirection Direction { get; init; } = KbDirection.RightToLeft;

    public static LightingProfile Solid(Rgb c, int brightness) =>
        new() { Zones = new[] { c, c, c, c }, Color = c, Brightness = brightness };

    // Garante 4 zonas e valores dentro do que a BIOS aceita.
    public LightingProfile Normalized()
    {
        var zones = new Rgb[ZoneCount];
        for (int i = 0; i < ZoneCount; i++) zones[i] = Zones is not null && i < Zones.Length ? Zones[i] : Color;
        return this with
        {
            Zones = zones,
            Brightness = Math.Clamp(Brightness, 0, MaxBrightness),
            Speed = Math.Clamp(Speed, 0, MaxSpeed),
            Direction = Direction == KbDirection.LeftToRight ? KbDirection.LeftToRight : KbDirection.RightToLeft,
            Effect = Enum.IsDefined(Effect) ? Effect : KbEffect.Static,
        };
    }
}
