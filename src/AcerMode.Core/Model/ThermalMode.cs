namespace AcerMode.Core;

// Códigos que a BIOS do PHN16-72 usa em GetGamingMiscSetting(0x0B).
public enum ThermalMode : byte { Quiet = 0, Balanced = 1, Performance = 4, Turbo = 5, Eco = 6 }

public static class ThermalModes
{
    public static readonly ThermalMode[] All =
        { ThermalMode.Quiet, ThermalMode.Balanced, ThermalMode.Performance, ThermalMode.Turbo, ThermalMode.Eco };

    public static bool IsKnown(int code) => code is >= 0 and <= 255 && Array.IndexOf(All, (ThermalMode)code) >= 0;

    public static string Description(ThermalMode m) => m switch
    {
        ThermalMode.Quiet => "Silencioso, ventoinhas baixas",
        ThermalMode.Balanced => "Equilíbrio entre ruído e desempenho",
        ThermalMode.Performance => "Mais potência para jogos e trabalho pesado",
        ThermalMode.Turbo => "Potência máxima, ventoinhas no talo",
        ThermalMode.Eco => "Economia de bateria",
        _ => "",
    };

    public static bool TryParse(string? s, out ThermalMode mode)
    {
        foreach (var m in All)
            if (string.Equals(m.ToString(), s?.Trim(), StringComparison.OrdinalIgnoreCase)) { mode = m; return true; }
        mode = ThermalMode.Balanced;
        return false;
    }
}
