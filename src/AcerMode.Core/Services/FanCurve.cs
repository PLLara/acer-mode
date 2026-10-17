namespace AcerMode.Core;

// Saída da curva: Boost = false deixa a BIOS no automático; true = Personalizado com Percent.
public readonly record struct FanCurveOutput(bool Boost, int Percent)
{
    public static readonly FanCurveOutput Bios = new(false, 0);
}

// Modo Inteligente: o automático da BIOS do PHN16-72 sobe pouco (95 °C a ~3700 rpm, contra
// ~7700 no máximo), então acima de 70 °C o app assume e sobe o % pela temperatura do chip
// mais quente. Abaixo de 64 °C devolve para a BIOS. Se o app fechar frio, as ventoinhas já
// estão no automático; se fechar quente, estão altas: nunca ficam baixas sob carga.
//
// Curva progressiva (2026-10-17, a primeira ia a 100% cedo demais):
//   70 °C → 40%   80 °C → 60%   88 °C → 80%   95 °C → 100%   (interpolada, passos de 5%)
// Sobe no máximo 10% por leitura (~2 s); a partir de 92 °C vai direto ao alvo, para a GPU
// não entrar em thermal slowdown. Desce 5% por leitura.
public static class FanCurve
{
    public const int StartC = 70, StopC = 64, EmergencyC = 92;
    public const int StartPercent = 40, StepPercent = 5, MaxRisePerReading = 10;

    static readonly (int C, int Percent)[] Points = { (70, 40), (80, 60), (88, 80), (95, 100) };

    public static int TargetPercent(int tempC)
    {
        if (tempC <= Points[0].C) return Points[0].Percent;
        for (int i = 1; i < Points.Length; i++)
        {
            var (c0, p0) = Points[i - 1];
            var (c1, p1) = Points[i];
            if (tempC <= c1)
            {
                double p = p0 + (tempC - c0) * (p1 - p0) / (double)(c1 - c0);
                return (int)Math.Round(p / StepPercent) * StepPercent;
            }
        }
        return 100;
    }

    public static FanCurveOutput Next(FanCurveOutput previous, int? cpuC, int? gpuC)
    {
        int? hottest = cpuC is null ? gpuC : gpuC is null ? cpuC : Math.Max(cpuC.Value, gpuC.Value);
        if (hottest is not { } t) return FanCurveOutput.Bios;   // sem leitura, a BIOS cuida

        bool boost = previous.Boost ? t > StopC : t >= StartC;
        if (!boost) return FanCurveOutput.Bios;

        int target = TargetPercent(t);
        int percent;
        if (t >= EmergencyC) percent = Math.Max(target, previous.Boost ? previous.Percent : 0);   // calor demais: direto
        else if (!previous.Boost) percent = StartPercent;                                         // assume sempre por baixo
        else if (target > previous.Percent) percent = Math.Min(target, previous.Percent + MaxRisePerReading);
        else percent = Math.Max(target, previous.Percent - StepPercent);                          // desce devagar
        return new FanCurveOutput(true, Math.Clamp(percent, StartPercent, 100));
    }
}
