namespace AcerMode.Core;

// Overclock da GPU pelo app. Limites bem abaixo do que o driver aceita (núcleo −1000..+1000,
// memória até +3000): o overclock do painel da NVIDIA derrubou este notebook em 2026-10.
public sealed record GpuOcSettings(bool Enabled = false, int CoreMhz = 0, int MemMhz = 0)
{
    public const int MaxCoreMhz = 150, CoreStep = 15, MaxMemMhz = 500, MemStep = 50;

    public GpuOcSettings Normalized() => this with
    {
        CoreMhz = Snap(CoreMhz, CoreStep, MaxCoreMhz),
        MemMhz = Snap(MemMhz, MemStep, MaxMemMhz),
    };

    static int Snap(int v, int step, int max) => Math.Clamp((int)Math.Round(v / (double)step) * step, 0, max);
}

public interface IGpuOverclock
{
    (int CoreMhz, int MemMhz)? Read();      // null = driver não respondeu
    void Apply(int coreMhz, int memMhz);    // lança HardwareException se o driver recusar
}

public sealed record OcStartupResult(bool DisabledBySafety, string? Message);
