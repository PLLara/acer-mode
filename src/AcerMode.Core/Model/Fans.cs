namespace AcerMode.Core;

// Auto/Max/Custom são os códigos da BIOS. Smart (Inteligente) existe só no app: a BIOS fica
// no Auto e o app passa para Personalizado quando esquenta (FanCurve). Nunca vai à BIOS.
public enum FanMode : byte { Auto = 1, Max = 2, Custom = 3, Smart = 8 }
public enum Fan : byte { Cpu = 0x01, Gpu = 0x04 }

public sealed record FanSettings(FanMode Mode = FanMode.Auto, int CpuPercent = 60, int GpuPercent = 60)
{
    public const int MinPercent = 30;

    public FanSettings Normalized() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : FanMode.Auto,
        CpuPercent = Math.Clamp(CpuPercent, MinPercent, 100),
        GpuPercent = Math.Clamp(GpuPercent, MinPercent, 100),
    };
}

public sealed record FanState(FanMode Cpu, FanMode Gpu, int? CpuPercent, int? GpuPercent);
