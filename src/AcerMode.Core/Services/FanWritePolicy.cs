namespace AcerMode.Core;

// Cada escrita de ventoinha é uma chamada à BIOS (~15 ms, congela núcleos), então o
// Inteligente não escreve a cada 5 % da curva: só mudanças de 10 % ou mais, subida para 100 %
// na hora, e descida só depois de 20 s da última escrita.
public static class FanWritePolicy
{
    public const int MinStep = 10;
    public static readonly TimeSpan DownHold = TimeSpan.FromSeconds(20);
    // Depois de assumir, o app fica no mínimo 30 s antes de devolver à BIOS: cada troca de
    // dono são 4 chamadas, e a temperatura perto do limiar ia e voltava em segundos.
    public static readonly TimeSpan ReleaseHold = TimeSpan.FromSeconds(30);

    public static bool ShouldWrite(FanCurveOutput applied, FanCurveOutput next, TimeSpan sinceLastWrite)
    {
        if (!applied.Boost && next.Boost) return true;
        if (applied.Boost && !next.Boost) return sinceLastWrite >= ReleaseHold;
        if (!next.Boost || next.Percent == applied.Percent) return false;
        if (next.Percent > applied.Percent) return next.Percent - applied.Percent >= MinStep || next.Percent == 100;
        return applied.Percent - next.Percent >= MinStep && sinceLastWrite >= DownHold;
    }
}
