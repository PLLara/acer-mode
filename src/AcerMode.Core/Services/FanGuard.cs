namespace AcerMode.Core;

public enum FanGuardAction { None, SwitchToSmart, DisableCustom }

// Ventoinha em % fixo pode cozinhar o notebook sob carga. O automático da BIOS sobe pouco
// (95 °C a ~3700 rpm), então o guard passa para o Inteligente, que vai a 100 % no calor.
// Sem leitura de temperatura nem a curva funciona: aí sim, volta ao automático da BIOS.
public static class FanGuard
{
    public const int LimitC = 85;

    public static FanGuardAction Evaluate(FanSettings active, SensorReading? r)
    {
        if (active.Mode is not (FanMode.Custom or FanMode.Smart)) return FanGuardAction.None;
        if (r is null || !r.HasTemps) return FanGuardAction.DisableCustom;   // sem leitura, sem proteção
        if (active.Mode == FanMode.Smart) return FanGuardAction.None;        // a curva já cuida do calor
        return r.CpuTempC > LimitC || r.GpuTempC > LimitC ? FanGuardAction.SwitchToSmart : FanGuardAction.None;
    }

    public static string Message(FanGuardAction a, SensorReading? r) => a switch
    {
        FanGuardAction.SwitchToSmart => $"Proteção ativou às {DateTime.Now:HH:mm}: CPU {r?.CpuTempC} °C, GPU {r?.GpuTempC} °C. Ventoinhas passaram para o Inteligente.",
        FanGuardAction.DisableCustom => "Sem leitura de temperatura: ventoinhas voltaram ao automático.",
        _ => "",
    };
}

// Marca em disco que existe ventoinha em Personalizado; sobrevive a um crash.
public sealed class DirtyMarker
{
    readonly string path;
    public DirtyMarker(string path) => this.path = path;
    public bool Exists => File.Exists(path);

    public void Set()
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, DateTime.Now.ToString("o"));
    }

    public void Clear() { try { File.Delete(path); } catch (IOException) { } }
}
