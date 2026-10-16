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
public static class FanCurve
{
    public const int StartC = 70, StopC = 64, FullC = 90;
    public const int StartPercent = 55, StepPercent = 5;

    public static FanCurveOutput Next(FanCurveOutput previous, int? cpuC, int? gpuC)
    {
        int? hottest = cpuC is null ? gpuC : gpuC is null ? cpuC : Math.Max(cpuC.Value, gpuC.Value);
        if (hottest is not { } t) return FanCurveOutput.Bios;   // sem leitura, a BIOS cuida

        bool boost = previous.Boost ? t > StopC : t >= StartC;
        if (!boost) return FanCurveOutput.Bios;

        double k = Math.Clamp((t - StartC) / (double)(FullC - StartC), 0, 1);
        int target = (int)Math.Round((StartPercent + k * (100 - StartPercent)) / StepPercent) * StepPercent;

        // sobe na hora; desce no máximo um passo por leitura, para não "bombear"
        int percent = previous.Boost && target < previous.Percent ? Math.Max(target, previous.Percent - StepPercent) : target;
        return new FanCurveOutput(true, Math.Clamp(percent, StartPercent, 100));
    }
}
