namespace AcerMode.Core;

public enum FanGuardAction { None, RevertToAuto, DisableCustom }

// Ventoinha em % fixo pode cozinhar o notebook sob carga; o guard devolve ao Auto.
public static class FanGuard
{
    public const int LimitC = 85;

    public static FanGuardAction Evaluate(FanSettings active, SensorReading? r)
    {
        if (active.Mode != FanMode.Custom) return FanGuardAction.None;
        if (r is null || !r.HasTemps) return FanGuardAction.DisableCustom;   // sem leitura, sem proteção
        return r.CpuTempC > LimitC || r.GpuTempC > LimitC ? FanGuardAction.RevertToAuto : FanGuardAction.None;
    }

    public static string Message(FanGuardAction a, SensorReading? r) => a switch
    {
        FanGuardAction.RevertToAuto => $"Proteção ativou às {DateTime.Now:HH:mm}: CPU {r?.CpuTempC} °C, GPU {r?.GpuTempC} °C. Ventoinhas voltaram ao automático.",
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
