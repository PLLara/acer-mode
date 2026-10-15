using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcerMode.Core;

public sealed class ModeProfile
{
    public LightingProfile Lighting { get; set; } = new();
    public FanSettings Fans { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public bool IndependentLighting { get; set; }
    public LightingProfile? GlobalLighting { get; set; }
    public bool ShowOsd { get; set; } = true;
    public CpuCapSettings CpuCap { get; set; } = new();
    public Dictionary<ThermalMode, ModeProfile> Profiles { get; set; } = new();

    // Campos de versões futuras sobrevivem a um load/save.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static AppSettings Defaults()
    {
        var s = new AppSettings();
        foreach (var m in ThermalModes.All) s.Profiles[m] = DefaultProfile(m);
        return s;
    }

    // As cores do Acer Mode antigo viram o padrão.
    public static ModeProfile DefaultProfile(ThermalMode m) => new()
    {
        Lighting = m switch
        {
            ThermalMode.Turbo => LightingProfile.Solid(new Rgb(255, 45, 10), 100),
            ThermalMode.Performance => LightingProfile.Solid(new Rgb(150, 70, 255), 100),
            ThermalMode.Quiet => LightingProfile.Solid(new Rgb(0, 190, 255), 45),
            ThermalMode.Eco => LightingProfile.Solid(new Rgb(40, 220, 90), 70),
            _ => LightingProfile.Solid(new Rgb(40, 120, 255), 100),
        },
        Fans = new FanSettings(),
    };

    public ModeProfile Profile(ThermalMode m)
    {
        if (!Profiles.TryGetValue(m, out var p) || p is null) Profiles[m] = p = DefaultProfile(m);
        return p;
    }

    public LightingProfile LightingFor(ThermalMode m) =>
        IndependentLighting && GlobalLighting is not null ? GlobalLighting : Profile(m).Lighting;

    public void SetIndependentLighting(bool on, ThermalMode active)
    {
        if (on && !IndependentLighting) GlobalLighting = Profile(active).Lighting;
        IndependentLighting = on;
    }

    public void Normalize()
    {
        Profiles ??= new();
        foreach (var key in Profiles.Keys.Where(k => !ThermalModes.IsKnown((int)k)).ToList()) Profiles.Remove(key);
        foreach (var m in ThermalModes.All)
        {
            var p = Profile(m);
            p.Lighting = (p.Lighting ?? DefaultProfile(m).Lighting).Normalized();
            p.Fans = (p.Fans ?? new FanSettings()).Normalized();
        }
        GlobalLighting = GlobalLighting?.Normalized();
        CpuCap = (CpuCap ?? new CpuCapSettings()).Normalized();
    }
}
